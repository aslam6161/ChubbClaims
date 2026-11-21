using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;

namespace ChubbClaims.Domain.Settings;

/// <summary>
/// Bound from the "ClaimRules" configuration section. Amounts are per market because each market
/// has its own currency (5,000 MYR and 5,000 IDR are very different reserves).
/// </summary>
public sealed class ClaimRulesSettings
{
    public const string SectionName = "ClaimRules";

    public int SlaDaysToDecision { get; set; } = 10;
    public int MaxIncidentAgeDays { get; set; } = 365;
    public Dictionary<Market, MarketClaimRules> Markets { get; set; } = [];

    /// <summary>The submission rules for a new claim in <paramref name="market"/> of <paramref name="type"/>.</summary>
    public ClaimSubmissionRules For(Market market, ClaimType type)
    {
        if (!Markets.TryGetValue(market, out var rules))
            throw new InvalidOperationException($"No claim rules configured for market {market} (ClaimRules:Markets:{market}).");

        var defaultReserve = type == ClaimType.Motor ? rules.DefaultMotorReserve : rules.DefaultPropertyReserve;
        return new ClaimSubmissionRules(defaultReserve, rules.HighPriorityThreshold, SlaDaysToDecision, MaxIncidentAgeDays);
    }
}

/// <summary>Per-market amounts, in that market's currency.</summary>
public sealed class MarketClaimRules
{
    public decimal DefaultMotorReserve { get; set; }
    public decimal DefaultPropertyReserve { get; set; }
    public decimal HighPriorityThreshold { get; set; }
}
