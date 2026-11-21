using System.Text.Json;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Messaging.Contracts;
using ChubbClaims.Messaging.Projections;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Repository.Outbox;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Messaging;

/// <summary>
/// Contract + projection: domain events → producer envelope (Repository) → consumer parse
/// (Messaging) → exposure delta. Proves both sides of the Kafka contract agree, without a broker.
/// </summary>
public class ExposureProjectionTests
{
    private static List<EventEnvelope> ThroughTheWire(Claim claim) =>
        claim.DomainEvents
            .Select(e => OutboxMessageFactory.Create(e, claim, correlationId: null).Payload)
            .Select(EventEnvelope.Parse)
            .ToList();

    private static ExposureDelta Sum(IEnumerable<EventEnvelope> envelopes) =>
        envelopes.Select(ExposureProjection.DeltaFor).OfType<ExposureDelta>()
            .Aggregate(new ExposureDelta(0, 0m), (a, d) => new ExposureDelta(a.OpenClaims + d.OpenClaims, a.OutstandingReserve + d.OutstandingReserve));

    [Fact]
    public void Envelope_parses_with_claim_context()
    {
        var claim = Submit();

        var envelope = ThroughTheWire(claim).Single();

        envelope.EventType.ShouldBe(ClaimEventTypes.ClaimSubmitted);
        envelope.EventVersion.ShouldBe(1);
        envelope.ClaimId.ShouldBe(claim.Id);
        (envelope.Market, envelope.Currency, envelope.ClaimType).ShouldBe((Market.MY, "MYR", ClaimType.Motor));
        envelope.DataAs<ClaimSubmittedData>().InitialReserve.ShouldBe(Estimate);
    }

    [Fact]
    public void Each_money_event_produces_the_documented_delta()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        claim.AdjustReserve(Officer, 15_000m, "Quote", Now);
        claim.Approve(Officer, Settlement, null, Now);
        claim.Settle(Officer, Now);

        var deltas = ThroughTheWire(claim).Select(e => (e.EventType, Delta: ExposureProjection.DeltaFor(e))).ToList();

        deltas.ShouldBe(
        [
            (ClaimEventTypes.ClaimSubmitted, new ExposureDelta(+1, Estimate)),
            (ClaimEventTypes.ClaimPickedUp, null),
            (ClaimEventTypes.ReserveAdjusted, new ExposureDelta(0, 15_000m - Estimate)),
            (ClaimEventTypes.ClaimApproved, new ExposureDelta(0, Settlement - 15_000m)),
            (ClaimEventTypes.ClaimSettled, new ExposureDelta(-1, -Settlement)),
        ]);
    }

    [Fact]
    public void Deltas_of_an_open_claim_add_up_to_its_outstanding_reserve()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);
        claim.AdjustReserve(Officer, 9_999.99m, "Revised", Now);

        Sum(ThroughTheWire(claim)).ShouldBe(new ExposureDelta(1, claim.OutstandingReserve));
    }

    [Fact]
    public void Deltas_of_a_closed_claim_add_up_to_zero()
    {
        Sum(ThroughTheWire(InStatus(ClaimStatus.Settled))).ShouldBe(new ExposureDelta(0, 0m));
        Sum(ThroughTheWire(InStatus(ClaimStatus.Rejected))).ShouldBe(new ExposureDelta(0, 0m));
    }

    [Fact]
    public void A_message_that_is_not_an_envelope_is_rejected_as_json()
    {
        Should.Throw<JsonException>(() => EventEnvelope.Parse("not json"));
        Should.Throw<JsonException>(() => EventEnvelope.Parse("null"));
    }

    [Fact]
    public void Unknown_extra_fields_are_ignored_so_additive_changes_stay_v1()
    {
        var claim = Submit();
        var payload = OutboxMessageFactory.Create(claim.DomainEvents.Single(), claim, null).Payload
            .Replace("\"data\":{", "\"futureField\":42,\"data\":{\"futureDataField\":\"x\",");

        var delta = ExposureProjection.DeltaFor(EventEnvelope.Parse(payload));

        delta.ShouldBe(new ExposureDelta(1, Estimate));
    }
}
