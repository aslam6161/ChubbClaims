namespace ChubbClaims.Domain.Events;

/// <summary>
/// A fact that happened to a claim. Collected on the aggregate, written to the outbox in the same
/// transaction as the claim change, then published to Kafka (claims.events.v1, key = ClaimId).
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    Guid ClaimId { get; }
    DateTimeOffset OccurredAt { get; }
}
