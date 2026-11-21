namespace ChubbClaims.Repository.Outbox;

/// <summary>
/// A domain event waiting to be published to Kafka. Written in the same transaction as the claim
/// change (transactional outbox), so an accepted claim change can never lose its event, and an
/// event is never published for a change that was rolled back.
/// Lives in Repository, not Domain: it is a persistence mechanism, not a business concept.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { } // EF Core

    public OutboxMessage(Guid id, string eventType, Guid claimId, string payload, string? correlationId, DateTimeOffset occurredAt)
    {
        Id = id;
        EventType = eventType;
        ClaimId = claimId;
        Payload = payload;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    /// <summary>Same as the event id, so a re-published message has the same id (consumers dedupe on it).</summary>
    public Guid Id { get; private set; }

    public string EventType { get; private set; } = null!;

    /// <summary>The Kafka message key: all events of one claim go to one partition, in order.</summary>
    public Guid ClaimId { get; private set; }

    /// <summary>The JSON event envelope (BACKEND.md §6), published as the Kafka message value.</summary>
    public string Payload { get; private set; } = null!;

    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    public void MarkPublished(DateTimeOffset at)
    {
        ProcessedAt = at;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;
    }
}
