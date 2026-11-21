using System.Text.Json;
using System.Text.Json.Serialization;
using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Messaging.Contracts;

/// <summary>
/// The consumer-side view of a claims.events.v1 message (BACKEND.md §6). Consumers depend only on
/// this contract, never on domain classes, so producer and consumer can evolve and deploy separately.
/// </summary>
public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    Guid ClaimId,
    string ClaimNumber,
    Guid? ClaimantId, // null on messages published before the field was added
    Market Market,
    string Currency,
    ClaimType ClaimType,
    JsonElement Data)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Throws <see cref="JsonException"/> for a message that is not a valid envelope.</summary>
    public static EventEnvelope Parse(string json) =>
        JsonSerializer.Deserialize<EventEnvelope>(json, JsonOptions) ?? throw new JsonException("Empty event envelope.");

    public T DataAs<T>() => Data.Deserialize<T>(JsonOptions) ?? throw new JsonException($"Empty data for {EventType}.");
}

/// <summary>Event type names (the event-type header and envelope field).</summary>
public static class ClaimEventTypes
{
    public const string ClaimSubmitted = nameof(ClaimSubmitted);
    public const string ClaimPickedUp = nameof(ClaimPickedUp);
    public const string ReserveAdjusted = nameof(ReserveAdjusted);
    public const string InformationRequested = nameof(InformationRequested);
    public const string InformationProvided = nameof(InformationProvided);
    public const string ClaimApproved = nameof(ClaimApproved);
    public const string ClaimSettled = nameof(ClaimSettled);
    public const string ClaimRejected = nameof(ClaimRejected);

    /// <summary>
    /// The claim's status right after this event, derived from the event type (each event is one
    /// transition). Null for events that do not change status (reserve adjustments) or unknown types.
    /// </summary>
    public static ClaimStatus? StatusAfter(string eventType) => eventType switch
    {
        ClaimSubmitted => ClaimStatus.Submitted,
        ClaimPickedUp or InformationProvided => ClaimStatus.UnderReview,
        InformationRequested => ClaimStatus.AwaitingInformation,
        ClaimApproved => ClaimStatus.Approved,
        ClaimSettled => ClaimStatus.Settled,
        ClaimRejected => ClaimStatus.Rejected,
        _ => null,
    };
}

// The "data" payloads this service consumes. Additive fields keep v1; unknown fields are ignored.
public sealed record ClaimSubmittedData(Guid ClaimantId, decimal InitialReserve, DateTimeOffset SlaDueAt);
public sealed record InformationRequestedData(Guid RequestId, string Question);
public sealed record InformationProvidedData(Guid RequestId, Guid? AssignedOfficerId);
public sealed record ReserveAdjustedData(decimal OldAmount, decimal NewAmount, string Reason);
public sealed record ClaimApprovedData(decimal SettlementAmount, decimal OldReserve, decimal NewReserve);
public sealed record ClaimSettledData(decimal PaidAmount, decimal ReleasedReserve);
public sealed record ClaimRejectedData(string Reason, decimal ReleasedReserve);
