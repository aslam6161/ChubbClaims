using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.ReadModels;

/// <summary>
/// Outstanding liability per market, currency and claim type. A read model written only by the
/// exposure projector from Kafka events, so managers get a portfolio-wide figure without
/// re-aggregating every open claim on each request. Eventually consistent: see <see cref="LastEventAt"/>.
/// Amounts are per currency; there is no FX conversion.
/// </summary>
public sealed class ExposureByMarket
{
    private ExposureByMarket() { } // EF Core

    public ExposureByMarket(Market market, string currency, ClaimType claimType, int openClaims, decimal outstandingReserve, DateTimeOffset lastEventAt)
    {
        Market = market;
        Currency = currency;
        ClaimType = claimType;
        OpenClaims = openClaims;
        OutstandingReserve = outstandingReserve;
        LastEventAt = lastEventAt;
    }

    public Market Market { get; private set; }
    public string Currency { get; private set; } = null!;
    public ClaimType ClaimType { get; private set; }
    public int OpenClaims { get; private set; }
    public decimal OutstandingReserve { get; private set; }

    /// <summary>When the newest event applied here happened: tells the UI how fresh the figure is.</summary>
    public DateTimeOffset LastEventAt { get; private set; }
}

/// <summary>
/// "Consumer group X has applied event Y". Written in the same transaction as the side effect,
/// so a redelivered event (Kafka is at-least-once) is recognised and skipped.
/// </summary>
public sealed class ProcessedEvent
{
    private ProcessedEvent() { } // EF Core

    public ProcessedEvent(string consumerGroup, Guid eventId, DateTimeOffset processedAt)
    {
        ConsumerGroup = consumerGroup;
        EventId = eventId;
        ProcessedAt = processedAt;
    }

    public string ConsumerGroup { get; private set; } = null!;
    public Guid EventId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
