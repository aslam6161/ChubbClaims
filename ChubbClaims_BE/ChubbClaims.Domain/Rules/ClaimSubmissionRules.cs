namespace ChubbClaims.Domain.Rules;

/// <summary>
/// Configured parameters for a new claim, resolved by the application layer for the claim's
/// market and type (appsettings: Reserves:Defaults, Priority:HighLossThresholds, Sla, Fnol).
/// </summary>
public sealed record ClaimSubmissionRules(
    decimal DefaultReserve,
    decimal HighPriorityThreshold,
    int SlaDaysToDecision,
    int MaxIncidentAgeDays);
