using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Service.DTOs.Policies;

/// <summary>A policy the claimant can claim on. Market and currency of the claim come from here.</summary>
public sealed record PolicyResponse(
    string PolicyNumber,
    ClaimType ProductType,
    Market Market,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    bool IsActive);
