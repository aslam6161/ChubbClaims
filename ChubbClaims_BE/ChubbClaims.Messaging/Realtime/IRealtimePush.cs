using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Messaging.Realtime;

/// <summary>
/// Pushes hints to connected browsers. Implemented by the host with SignalR; consumers only know
/// this interface, so Messaging does not depend on ASP.NET Core. A push is a hint: clients
/// re-fetch over REST, so a lost push only delays a refresh.
/// </summary>
public interface IRealtimePush
{
    /// <summary>To the claimant who owns the claim (if known) and to staff in the claim's market.</summary>
    Task ClaimUpdatedAsync(ClaimUpdatedMessage message, Guid? claimantId, CancellationToken ct);

    /// <summary>To managers (exposure is portfolio-wide).</summary>
    Task ExposureUpdatedAsync(ExposureUpdatedMessage message, CancellationToken ct);
}

/// <summary>Status is the status after the event; null when the event does not change it (reserve adjusted).</summary>
public sealed record ClaimUpdatedMessage(
    Guid ClaimId, string ClaimNumber, Market Market, string EventType, ClaimStatus? Status, DateTimeOffset OccurredAt);

public sealed record ExposureUpdatedMessage(Market Market, string Currency, ClaimType ClaimType, DateTimeOffset OccurredAt);
