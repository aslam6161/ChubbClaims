using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Events;

namespace ChubbClaims.Repository.Outbox;

/// <summary>
/// Turns a domain event into the Kafka event envelope (BACKEND.md §6):
/// <code>{ eventId, eventType, eventVersion, occurredAt, claimId, claimNumber, market, currency, claimType, data }</code>
/// The envelope carries the claim context every consumer needs (market, currency, type), so
/// consumers never have to call back into the API.
/// </summary>
public static class OutboxMessageFactory
{
    /// <summary>Additive changes keep v1; a breaking change means a new version (and topic).</summary>
    public const int EventVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] EnvelopeFields = ["eventId", "claimId", "occurredAt"];

    public static OutboxMessage Create(IDomainEvent domainEvent, Claim claim, string? correlationId)
    {
        var eventType = domainEvent.GetType().Name;

        var data = JsonSerializer.SerializeToNode(domainEvent, domainEvent.GetType(), JsonOptions)!.AsObject();
        foreach (var field in EnvelopeFields)
            data.Remove(field); // already on the envelope

        var envelope = new JsonObject
        {
            ["eventId"] = domainEvent.EventId,
            ["eventType"] = eventType,
            ["eventVersion"] = EventVersion,
            ["occurredAt"] = domainEvent.OccurredAt,
            ["claimId"] = claim.Id,
            ["claimNumber"] = claim.ClaimNumber,
            ["market"] = claim.Market.ToString(),
            ["currency"] = claim.Currency,
            ["claimType"] = claim.Type.ToString(),
            ["data"] = data,
        };

        return new OutboxMessage(
            domainEvent.EventId, eventType, claim.Id, envelope.ToJsonString(JsonOptions), correlationId, domainEvent.OccurredAt);
    }
}
