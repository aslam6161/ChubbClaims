using ChubbClaims.SmokeTests.Infrastructure;
using static ChubbClaims.SmokeTests.Infrastructure.Users;

namespace ChubbClaims.SmokeTests;

/// <summary>Every officer action over HTTP: who may do it, in which state, with which input, and the race.</summary>
public class OfficerActionTests
{
    [SmokeFact]
    public async Task A_claim_goes_from_FNOL_to_settled_with_its_full_history()
    {
        var claim = await Claims.Submit(estimate: 10_000m);
        (await Api.Post($"/api/claims/{claim.Id}/pick-up", OfficerMy)).Status.ShouldBe(HttpStatusCode.NoContent, "pick up");
        (await Api.Get("/api/work-queue?view=mine&pageSize=100", OfficerMy)).Json.List()
            .ShouldContain(r => r.Id("id") == claim.Id, "now in the officer's queue");

        (await Api.Put($"/api/claims/{claim.Id}/reserve", OfficerMy, new { amount = 12_500, reason = "Workshop quote" }))
            .Status.ShouldBe(HttpStatusCode.NoContent, "adjust reserve");
        (await Api.Post($"/api/claims/{claim.Id}/information-requests", OfficerMy, new { question = "Please upload photos." }))
            .Status.ShouldBe(HttpStatusCode.NoContent, "request information");
        (await Claims.AnswerOpenRequest(claim.Id, "Uploaded.")).Status.ShouldBe(HttpStatusCode.NoContent, "claimant answers");
        (await Api.Post($"/api/claims/{claim.Id}/notes", ManagerMy, new { text = "Checked with workshop." }))
            .Status.ShouldBe(HttpStatusCode.NoContent, "a manager adds a note");
        (await Api.Post($"/api/claims/{claim.Id}/approve", OfficerMy, new { settlementAmount = 11_800, note = "Agreed" }))
            .Status.ShouldBe(HttpStatusCode.NoContent, "approve");
        (await Api.Post($"/api/claims/{claim.Id}/settle", OfficerMy)).Status.ShouldBe(HttpStatusCode.NoContent, "settle");

        var staffView = await Claims.Detail(claim.Id, OfficerMy);
        staffView.Str("status").ShouldBe("Settled");
        staffView.Num("paidAmount").ShouldBe(11_800m);
        staffView.Num("outstandingReserve").ShouldBe(0m);
        staffView.List("statusHistory").Select(h => h.Str("to")).ShouldBe(
            new[] { "Submitted", "UnderReview", "AwaitingInformation", "UnderReview", "Approved", "Settled" });
        staffView.List("notes").Length.ShouldBe(2, "the manager's note and the approval note");

        var claimantView = await Claims.Detail(claim.Id, ClaimantMy);
        claimantView.Num("settlementAmount").ShouldBe(11_800m, "the claimant sees the decision");
        claimantView.IsNull("notes").ShouldBeTrue("but not internal notes");
    }

    [SmokeFact]
    public async Task Only_staff_in_the_claims_market_can_pick_it_up()
    {
        var claim = await Claims.Submit();
        (await Api.Post($"/api/claims/{claim.Id}/pick-up", OfficerSg)).Status.ShouldBe(HttpStatusCode.NotFound, "SG officer, MY claim");
        (await Api.Post($"/api/claims/{claim.Id}/pick-up", ClaimantMy)).Status.ShouldBe(HttpStatusCode.Forbidden, "the claimant");
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task A_manager_who_is_not_assigned_cannot_adjust_the_reserve()
    {
        var claim = await Claims.UnderReview();
        var response = await Api.Put($"/api/claims/{claim.Id}/reserve", ManagerMy, new { amount = 12_000, reason = "Quote" });
        response.Status.ShouldBe(HttpStatusCode.Forbidden, response.ToString());
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Actions_out_of_order_are_409()
    {
        var claim = await Claims.Submit();
        (await Api.Post($"/api/claims/{claim.Id}/approve", OfficerMy, new { settlementAmount = 9_000 }))
            .Status.ShouldBe(HttpStatusCode.Conflict, "approve before pick-up");

        await Api.Post($"/api/claims/{claim.Id}/pick-up", OfficerMy);
        (await Api.Post($"/api/claims/{claim.Id}/settle", OfficerMy)).Status.ShouldBe(HttpStatusCode.Conflict, "settle before approval");

        await Api.Post($"/api/claims/{claim.Id}/information-requests", OfficerMy, new { question = "First?" });
        (await Api.Post($"/api/claims/{claim.Id}/information-requests", OfficerMy, new { question = "Another?" }))
            .Status.ShouldBe(HttpStatusCode.Conflict, "a second open question");

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Bad_input_is_refused_with_400_or_422()
    {
        var claim = await Claims.UnderReview();
        (await Api.Put($"/api/claims/{claim.Id}/reserve", OfficerMy, new { amount = -5, reason = "Typo" }))
            .Status.ShouldBe(HttpStatusCode.UnprocessableEntity, "negative reserve");
        (await Api.Put($"/api/claims/{claim.Id}/reserve", OfficerMy, new { reason = "No amount" }))
            .Status.ShouldBe(HttpStatusCode.BadRequest, "missing amount");
        (await Api.Post($"/api/claims/{claim.Id}/approve", OfficerMy, new { settlementAmount = 0 }))
            .Status.ShouldBe(HttpStatusCode.UnprocessableEntity, "zero settlement");
        (await Api.Post($"/api/claims/{claim.Id}/reject", OfficerMy, new { reason = "" }))
            .Status.ShouldBe(HttpStatusCode.BadRequest, "reject without a reason");
        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Rejecting_releases_the_outstanding_reserve()
    {
        var claim = await Claims.UnderReview();
        (await Api.Post($"/api/claims/{claim.Id}/reject", OfficerMy, new { reason = "Excluded: racing" }))
            .Status.ShouldBe(HttpStatusCode.NoContent);
        (await Claims.Detail(claim.Id, OfficerMy)).Num("outstandingReserve").ShouldBe(0m);
    }

    [SmokeFact]
    public async Task Ten_simultaneous_pick_ups_each_have_exactly_one_winner()
    {
        var losers = new Dictionary<string, int>();
        for (var round = 0; round < 10; round++)
        {
            var claim = await Claims.Submit();
            using var start = new Barrier(2);
            var picks = new[] { OfficerMy, ManagerMy }.Select(user => Task.Run(async () =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken); // both requests leave together
                return (User: user, Response: await Api.Post($"/api/claims/{claim.Id}/pick-up", user));
            })).ToArray();
            var results = await Task.WhenAll(picks);

            results.Select(r => r.Response.Status).ShouldBe(
                new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }, ignoreOrder: true, customMessage: $"round {round + 1}");
            var loser = results.Single(r => r.Response.Status == HttpStatusCode.Conflict).Response;
            var why = loser.Detail?.Contains("changed by someone else") == true ? "row version conflict" : "already under review";
            losers[why] = losers.GetValueOrDefault(why) + 1;

            await Claims.Close(claim.Id, assignee: results.Single(r => r.Response.Status == HttpStatusCode.NoContent).User);
        }
        Api.Log($"How the loser lost: {string.Join(", ", losers.Select(l => $"{l.Key} {l.Value}"))}");
    }
}
