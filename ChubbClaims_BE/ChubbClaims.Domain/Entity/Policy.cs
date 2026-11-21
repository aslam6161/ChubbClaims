using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;

namespace ChubbClaims.Domain.Entity;

/// <summary>
/// Stub of a policy that in reality lives in an external policy-administration system.
/// Seeded for the demo; a claim takes its market and currency from its policy, never from user input.
/// </summary>
public sealed class Policy
{
    private Policy() { } // EF Core

    public Policy(
        string policyNumber, Guid holderUserId, Market market, string currency, ClaimType productType,
        DateOnly effectiveFrom, DateOnly effectiveTo, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(policyNumber))
            throw new DomainValidationException("Policy number is required.");
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
            throw new DomainValidationException("Currency must be an ISO 4217 code such as MYR.");
        if (effectiveTo < effectiveFrom)
            throw new DomainValidationException("Policy end date is before its start date.");

        PolicyNumber = policyNumber.Trim();
        HolderUserId = holderUserId;
        Market = market;
        Currency = currency;
        ProductType = productType;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }

    public string PolicyNumber { get; private set; } = null!;
    public Guid HolderUserId { get; private set; }
    public Market Market { get; private set; }
    public string Currency { get; private set; } = null!;
    public ClaimType ProductType { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }

    public bool Covers(DateOnly date) => IsActive && date >= EffectiveFrom && date <= EffectiveTo;
}
