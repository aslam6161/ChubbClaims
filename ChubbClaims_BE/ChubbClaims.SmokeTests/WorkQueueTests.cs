using ChubbClaims.SmokeTests.Infrastructure;
using static ChubbClaims.SmokeTests.Infrastructure.Users;

namespace ChubbClaims.SmokeTests;

/// <summary>The officer's work queue: what's in it, its order, filters, paging and search.</summary>
public class WorkQueueTests
{
    [SmokeFact]
    public async Task Unassigned_queue_holds_only_the_markets_unassigned_claims_most_urgent_first()
    {
        var claim = await Claims.Submit(); // so the queue isn't empty
        var rows = (await Api.Get("/api/work-queue?view=unassigned&pageSize=100", OfficerMy)).Json.List();

        rows.ShouldContain(r => r.Id("id") == claim.Id);
        rows.ShouldAllBe(r => r.Str("market") == "MY" && r.IsNull("assignedOfficerId"), "only MY, only unassigned");

        var due = rows.Select(r => r["slaDueAt"]!.GetValue<DateTimeOffset>()).ToList();
        due.ShouldBe(due.Order().ToList(), "sorted by decision due date");
        var overdue = rows.Select(r => r.Bool("isOverdue")).ToList();
        overdue.ShouldBe(overdue.OrderDescending().ToList(), "overdue claims come first");

        rows.ShouldAllBe(r => r.Str("waitingOn") == "Officer" && r.Num("outstandingReserve") > 0 && r.Str("currency") == "MYR",
            "each row carries waiting-on, outstanding reserve and currency");
        rows.ShouldAllBe(r => r.Int("ageDays") >= 0);

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task My_queue_shows_open_claims_and_who_each_waits_on_and_the_status_filter_shows_closed_ones()
    {
        var reviewing = await Claims.UnderReview();
        var waiting = await Claims.AwaitingClaimant();
        var mine = (await Api.Get("/api/work-queue?view=mine&pageSize=100", OfficerMy)).Json.List();

        mine.ShouldAllBe(r => r.Str("status") != "Settled" && r.Str("status") != "Rejected", "open claims only");
        mine.Single(r => r.Id("id") == reviewing.Id).Str("waitingOn").ShouldBe("Officer");
        mine.Single(r => r.Id("id") == waiting.Id).Str("waitingOn").ShouldBe("Claimant");

        await Claims.Close(reviewing.Id);
        var rejected = (await Api.Get("/api/work-queue?view=mine&status=Rejected&pageSize=100", OfficerMy)).Json.List();
        var row = rejected.Where(r => r.Id("id") == reviewing.Id).ShouldHaveSingleItem();
        row.Num("outstandingReserve").ShouldBe(0m, "a closed claim has nothing outstanding");
        rejected.ShouldAllBe(r => r.Str("status") == "Rejected");

        await Claims.Close(waiting.Id);
    }

    [SmokeFact]
    public async Task Each_market_sees_only_its_own_claims()
    {
        var fnol = Claims.MotorFnol();
        fnol["policyNumber"] = "POL-SG-M-0001"; // claimant.sg's motor policy
        var sgClaim = (await Api.Post("/api/claims", ClaimantSg, fnol)).Json.Id();
        var myClaim = await Claims.Submit();

        var rows = (await Api.Get("/api/work-queue?view=unassigned&pageSize=100", OfficerSg)).Json.List();
        rows.ShouldContain(r => r.Id("id") == sgClaim);
        rows.ShouldAllBe(r => r.Str("market") == "SG");
        rows.ShouldNotContain(r => r.Id("id") == myClaim.Id);

        await Claims.Close(sgClaim, assignee: OfficerSg);
        await Claims.Close(myClaim.Id);
    }

    [SmokeFact]
    public async Task Claimants_cannot_read_the_queue_and_an_unknown_view_is_400()
    {
        (await Api.Get("/api/work-queue", ClaimantMy)).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await Api.Get("/api/work-queue?view=everything", OfficerMy)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SmokeFact]
    public async Task Pages_follow_on_with_no_overlap_and_the_total_counts_the_whole_queue()
    {
        var opened = new[] { await Claims.UnderReview(), await Claims.UnderReview(), await Claims.UnderReview() };
        var everything = (await Api.Get("/api/work-queue?view=mine&pageSize=100", OfficerMy)).Json;
        var first = (await Api.Get("/api/work-queue?view=mine&pageSize=2&page=1", OfficerMy)).Json;
        var second = (await Api.Get("/api/work-queue?view=mine&pageSize=2&page=2", OfficerMy)).Json;

        static IEnumerable<string> Numbers(JsonNode page) => page.List().Select(r => r.Str("claimNumber"));
        Numbers(first).Concat(Numbers(second)).ShouldBe(Numbers(everything).Take(4));
        first.Int("totalCount").ShouldBe(everything.Int("totalCount"));
        first.Int("totalCount").ShouldBeGreaterThanOrEqualTo(3);

        foreach (var claim in opened) await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Search_finds_a_claim_by_number_and_the_overdue_count_still_covers_the_whole_queue()
    {
        var claim = await Claims.Submit();
        var whole = (await Api.Get("/api/work-queue?view=unassigned", OfficerMy)).Json;
        var found = (await Api.Get($"/api/work-queue?view=unassigned&search={claim.Number[^6..]}", OfficerMy)).Json;

        found.List().Select(r => r.Str("claimNumber")).ShouldBe(new[] { claim.Number });
        found.Int("overdueCount").ShouldBe(whole.Int("overdueCount"));

        await Claims.Close(claim.Id);
    }

    [SmokeTheory]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    [InlineData("page=2147483647")]
    public async Task Out_of_range_paging_is_400_never_500(string query) =>
        (await Api.Get($"/api/work-queue?{query}", OfficerMy)).Status.ShouldBe(HttpStatusCode.BadRequest);
}
