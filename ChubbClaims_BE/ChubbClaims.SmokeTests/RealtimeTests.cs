using ChubbClaims.SmokeTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using static ChubbClaims.SmokeTests.Infrastructure.Users;

namespace ChubbClaims.SmokeTests;

/// <summary>SignalR pushes (with the .NET client), in-app notifications and the team workload.</summary>
public class RealtimeTests
{
    [SmokeFact]
    public async Task The_hub_refuses_a_connection_without_a_token()
    {
        await using var anonymous = HubListener.Build(token: null);
        var refused = await Should.ThrowAsync<HttpRequestException>(() => anonymous.StartAsync(TestContext.Current.CancellationToken));
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SmokeFact]
    public async Task Claim_changes_are_pushed_to_the_people_concerned_and_nobody_else()
    {
        var users = new[] { ClaimantMy, ClaimantSg, OfficerMy, OfficerSg, ManagerMy };
        var hubs = (await Task.WhenAll(users.Select(HubListener.Connect))).ToDictionary(h => h.User);
        try
        {
            hubs.Values.ShouldAllBe(h => h.Connection.State == HubConnectionState.Connected);

            var sent = DateTime.UtcNow;
            var claim = await Claims.Submit(estimate: 2_500m, description: "Realtime smoke test");

            var own = await hubs[ClaimantMy].WaitFor(m => m.IsAbout(claim.Id, "Submitted"));
            own.ShouldNotBeNull("the claimant gets ClaimUpdated(Submitted)");
            Api.Log($"Pushed to the claimant {(own.At - sent).TotalMilliseconds:F0} ms after the FNOL was sent");
            (await hubs[OfficerMy].WaitFor(m => m.IsAbout(claim.Id, "Submitted"))).ShouldNotBeNull("MY officers (group staff:MY)");
            (await hubs[ManagerMy].WaitFor(m => m.IsAbout(claim.Id, "Submitted"))).ShouldNotBeNull("the MY manager");
            (await hubs[ManagerMy].WaitFor(m => m.Type == "ExposureUpdated" && m.Get("market") == "MY" && m.Get("claimType") == "Motor"))
                .ShouldNotBeNull("the MY manager gets ExposureUpdated for MY/Motor");

            await Api.Post($"/api/claims/{claim.Id}/pick-up", OfficerMy);
            (await hubs[ClaimantMy].WaitFor(m => m.IsAbout(claim.Id, "UnderReview"))).ShouldNotBeNull("claimant: UnderReview after pick-up");

            await Api.Put($"/api/claims/{claim.Id}/reserve", OfficerMy, new { amount = 3_000, reason = "Quote" });
            var reserve = await hubs[OfficerMy].WaitFor(m => m.IsAbout(claim.Id) && m.Get("eventType") == "ReserveAdjusted");
            reserve.ShouldNotBeNull("staff hear about the reserve change");
            reserve.IsNull("status").ShouldBeTrue("status is null: unchanged");

            await Api.Post($"/api/claims/{claim.Id}/information-requests", OfficerMy, new { question = "Photos of the damage, please." });
            (await hubs[ClaimantMy].WaitFor(m => m.IsAbout(claim.Id, "AwaitingInformation"))).ShouldNotBeNull("claimant: AwaitingInformation");

            await Task.Delay(1000, TestContext.Current.CancellationToken); // time for anything misrouted to arrive
            hubs[ClaimantSg].Received.ShouldNotContain(m => m.IsAbout(claim.Id, null), "another claimant");
            hubs[OfficerSg].Received.ShouldNotContain(m => m.IsAbout(claim.Id, null), "an officer in another market");
            hubs[OfficerMy].Received.ShouldNotContain(m => m.Type == "ExposureUpdated", "exposure is for managers only");

            await Claims.Close(claim.Id);
        }
        finally
        {
            foreach (var hub in hubs.Values) await hub.DisposeAsync();
        }
    }

    [SmokeFact]
    public async Task Notifications_tell_each_side_what_happened_and_can_be_marked_read()
    {
        var claim = await Claims.UnderReview();
        await Api.Put($"/api/claims/{claim.Id}/reserve", OfficerMy, new { amount = 3_000, reason = "Quote" });
        await Api.Post($"/api/claims/{claim.Id}/information-requests", OfficerMy, new { question = "Photos of the damage, please." });

        // Received, being reviewed, information needed. Never the reserve: that's internal.
        var forClaimant = await NotificationsAbout(claim.Id, ClaimantMy, count: 3);
        forClaimant.Length.ShouldBe(3, string.Join(" | ", forClaimant.Select(n => n.Str("message"))));
        forClaimant.ShouldNotContain(n => n.Str("message").Contains("3,000") || n.Str("message").Contains("3000")
            || n.Str("message").Contains("reserve", StringComparison.OrdinalIgnoreCase));

        await Claims.AnswerOpenRequest(claim.Id, "Uploaded.");
        var forOfficer = await NotificationsAbout(claim.Id, OfficerMy, count: 2);
        forOfficer.Length.ShouldBe(2, string.Join(" | ", forOfficer.Select(n => n.Str("message"))));
        forOfficer.ShouldContain(n => n.Str("message").Contains("waiting to be picked up"), "told of the new claim");
        forOfficer.ShouldContain(n => n.Str("message").Contains("answered your question"), "and that the claimant answered");

        var target = forClaimant[0].Id();
        (await Api.Post($"/api/notifications/{target}/read", ClaimantSg)).Status
            .ShouldBe(HttpStatusCode.NotFound, "marking someone else's notification");

        var before = await Api.Settled(() => UnreadCount(ClaimantMy), (a, b) => a == b, TimeSpan.FromSeconds(1));
        (await Api.Post($"/api/notifications/{target}/read", ClaimantMy)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadCount(ClaimantMy)).ShouldBe(before - 1, "marking one read drops the unread count by one");

        (await Api.Post("/api/notifications/read-all", ClaimantMy)).Status.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadCount(ClaimantMy)).ShouldBe(0, "read-all");

        await Claims.Close(claim.Id);
    }

    [SmokeFact]
    public async Task Team_workload_shows_the_markets_staff_with_counts_that_match_their_queues()
    {
        var team = await Api.Get("/api/team/workload", OfficerMy);
        team.Status.ShouldBe(HttpStatusCode.OK);
        team.Json.Str("market").ShouldBe("MY");
        var staff = team.Json.List("officers");
        staff.Length.ShouldBe(2, "the MY officer and the MY manager");

        var me = staff.Single(o => o.Str("name") == "Hafiz Ismail");
        var queue = (await Api.Get("/api/work-queue?view=mine", OfficerMy)).Json;
        me.Int("openClaims").ShouldBe(queue.Int("totalCount"), "the officer's open count matches their work queue");
        (me.Int("underReview") + me.Int("awaitingClaimant") + me.Int("approvedAwaitingPayment")).ShouldBe(me.Int("openClaims"));

        (await Api.Get("/api/team/workload", ClaimantMy)).Status.ShouldBe(HttpStatusCode.Forbidden, "claimants");
    }

    private static async Task<JsonNode[]> NotificationsAbout(Guid claimId, string user, int count)
    {
        async Task<JsonNode[]> Read() =>
            (await Api.Get("/api/notifications", user)).Json.List().Where(n => n.Id("claimId") == claimId).ToArray();

        // They come from Kafka a moment after the request; wait for them, then once more for any extras.
        await Api.WaitFor(async () => await Read() is { } list && list.Length >= count ? list : null);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        return await Read();
    }

    private static async Task<int> UnreadCount(string user) => (await Api.Get("/api/notifications", user)).Json.Int("unreadCount");
}
