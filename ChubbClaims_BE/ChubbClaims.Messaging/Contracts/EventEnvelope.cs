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
}

// The "data" payloads this service consumes. Additive fields keep v1; unknown fields are ignored.
public sealed record ClaimSubmittedData(Guid ClaimantId, decimal InitialReserve, DateTimeOffset SlaDueAt);
public sealed record ReserveAdjustedData(decimal OldAmount, decimal NewAmount, string Reason);
public sealed record ClaimApprovedData(decimal SettlementAmount, decimal OldReserve, decimal NewReserve);
public sealed record ClaimSettledData(decimal PaidAmount, decimal ReleasedReserve);
public sealed record ClaimRejectedData(string Reason, decimal ReleasedReserve);
