using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.ReadModels;
using ChubbClaims.Messaging.Contracts;
using ChubbClaims.Messaging.Projections;
using ChubbClaims.Repository.Outbox;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Messaging;

/// <summary>Domain events → envelope on the wire → who gets which notification.</summary>
public class NotificationProjectionTests
{
    private static List<EventEnvelope> ThroughTheWire(Claim claim) =>
        claim.DomainEvents.Select(e => EventEnvelope.Parse(OutboxMessageFactory.Create(e, claim, null).Payload)).ToList();

    private static List<Notification> NotificationsFor(Claim claim) =>
        ThroughTheWire(claim).SelectMany(NotificationProjection.For).ToList();

    [Fact]
    public void Claimant_hears_about_each_step_of_an_approved_and_paid_claim()
    {
        var notifications = NotificationsFor(InStatus(ClaimStatus.Settled));

        notifications.ShouldAllBe(n => n.UserId == Claimant.UserId);
        notifications.Select(n => n.Message).ShouldBe(
        [
            "We received your claim CLM-MY-2026-000001. We aim to reach a decision by 1 Dec 2026.",
            "Your claim CLM-MY-2026-000001 is now being reviewed by a claims officer.",
            "Your claim CLM-MY-2026-000001 was approved for MYR 10,000.00.",
            "Payment of MYR 10,000.00 for claim CLM-MY-2026-000001 has been made.",
        ]);
    }

    [Fact]
    public void Claimant_sees_the_question_and_the_officer_hears_the_answer()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);
        claim.ProvideInformation(Claimant, claim.OpenInformationRequest!.Id, "Uploaded", Now);

        var notifications = NotificationsFor(claim);

        notifications.ShouldContain(n => n.UserId == Claimant.UserId && n.Message.StartsWith("We need more information for claim"));
        notifications.Last().UserId.ShouldBe(Officer.UserId);
        notifications.Last().Message.ShouldBe("The claimant answered your question on claim CLM-MY-2026-000001.");
    }

    [Fact]
    public void Reserve_changes_never_reach_the_claimant()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        claim.ClearDomainEvents();
        claim.AdjustReserve(Officer, 12_345m, "Quote", Now);

        NotificationsFor(claim).ShouldBeEmpty();
    }

    [Fact]
    public void Rejection_tells_the_claimant_the_reason()
    {
        var rejected = NotificationsFor(InStatus(ClaimStatus.Rejected)).Last();

        rejected.UserId.ShouldBe(Claimant.UserId);
        rejected.Message.ShouldStartWith("Your claim CLM-MY-2026-000001 was not approved: ");
    }

    [Fact]
    public void Envelope_without_claimant_id_produces_no_claimant_notification()
    {
        var claim = Submit();
        var payload = OutboxMessageFactory.Create(claim.DomainEvents.Single(), claim, null).Payload
            .Replace($"\"claimantId\":\"{Claimant.UserId}\",", ""); // a message from before the field existed

        NotificationProjection.For(EventEnvelope.Parse(payload)).ShouldBeEmpty();
    }

    [Fact]
    public void Long_free_text_is_truncated_to_fit_the_column()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        claim.ClearDomainEvents();
        claim.RequestInformation(Officer, new string('?', 1_500), Now);

        var message = NotificationsFor(claim).Single().Message;

        message.Length.ShouldBe(Notification.MaxMessageLength);
        message.ShouldEndWith("…");
    }

    [Fact]
    public void Status_after_each_event_matches_the_claim_lifecycle()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);
        claim.ProvideInformation(Claimant, claim.OpenInformationRequest!.Id, "Uploaded", Now);
        claim.AdjustReserve(Officer, 7_000m, "Revised", Now);
        claim.Reject(Officer, "Excluded", Now);

        ThroughTheWire(claim).Select(e => ClaimEventTypes.StatusAfter(e.EventType)).ShouldBe(
        [
            ClaimStatus.Submitted, ClaimStatus.UnderReview, ClaimStatus.AwaitingInformation,
            ClaimStatus.UnderReview, null, ClaimStatus.Rejected,
        ]);
        claim.Status.ShouldBe(ClaimStatus.Rejected);
    }
}
