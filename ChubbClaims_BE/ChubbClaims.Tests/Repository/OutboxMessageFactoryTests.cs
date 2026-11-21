using System.Text.Json;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Events;
using ChubbClaims.Repository.Outbox;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Repository;

/// <summary>The Kafka event envelope (BACKEND.md §6) produced for each domain event.</summary>
public class OutboxMessageFactoryTests
{
    [Fact]
    public void Envelope_carries_claim_context_and_event_specific_data()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        claim.Approve(Officer, Settlement, null, Now);
        var approved = claim.DomainEvents.OfType<ClaimApproved>().Single();

        var message = OutboxMessageFactory.Create(approved, claim, correlationId: "req-42");

        message.Id.ShouldBe(approved.EventId); // stable id: consumers dedupe on it
        message.EventType.ShouldBe("ClaimApproved");
        message.ClaimId.ShouldBe(claim.Id); // Kafka key
        message.CorrelationId.ShouldBe("req-42");
        message.ProcessedAt.ShouldBeNull();

        var root = JsonDocument.Parse(message.Payload).RootElement;
        root.GetProperty("eventId").GetGuid().ShouldBe(approved.EventId);
        root.GetProperty("eventType").GetString().ShouldBe("ClaimApproved");
        root.GetProperty("eventVersion").GetInt32().ShouldBe(1);
        root.GetProperty("claimId").GetGuid().ShouldBe(claim.Id);
        root.GetProperty("claimNumber").GetString().ShouldBe("CLM-MY-2026-000001");
        root.GetProperty("claimantId").GetGuid().ShouldBe(Claimant.UserId);
        root.GetProperty("market").GetString().ShouldBe("MY");
        root.GetProperty("currency").GetString().ShouldBe("MYR");
        root.GetProperty("claimType").GetString().ShouldBe("Motor");

        var data = root.GetProperty("data");
        data.GetProperty("settlementAmount").GetDecimal().ShouldBe(Settlement);
        data.GetProperty("oldReserve").GetDecimal().ShouldBe(Estimate);
        data.GetProperty("newReserve").GetDecimal().ShouldBe(Settlement);
    }

    [Fact]
    public void Envelope_fields_are_not_repeated_inside_data()
    {
        var claim = Submit();
        var submitted = claim.DomainEvents.Single();

        var data = JsonDocument.Parse(OutboxMessageFactory.Create(submitted, claim, null).Payload).RootElement.GetProperty("data");

        data.TryGetProperty("eventId", out _).ShouldBeFalse();
        data.TryGetProperty("claimId", out _).ShouldBeFalse();
        data.TryGetProperty("occurredAt", out _).ShouldBeFalse();
        data.GetProperty("claimantId").GetGuid().ShouldBe(Claimant.UserId);
        data.GetProperty("initialReserve").GetDecimal().ShouldBe(Estimate);
    }

    [Fact]
    public void Every_claim_event_type_produces_a_valid_envelope()
    {
        var claim = InStatus(ClaimStatus.Settled); // submit, pick up, approve, settle

        var types = claim.DomainEvents
            .Select(e => OutboxMessageFactory.Create(e, claim, null))
            .Select(m => JsonDocument.Parse(m.Payload).RootElement.GetProperty("eventType").GetString())
            .ToList();

        types.ShouldBe(["ClaimSubmitted", "ClaimPickedUp", "ClaimApproved", "ClaimSettled"]);
    }
}
