namespace ChubbClaims.Repository.Interfaces;

/// <summary>
/// The correlation id of the current operation (the HTTP request's X-Correlation-Id), copied onto
/// outbox rows so it travels with the event into Kafka headers and consumer logs.
/// Null outside a request (e.g. while seeding).
/// </summary>
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
