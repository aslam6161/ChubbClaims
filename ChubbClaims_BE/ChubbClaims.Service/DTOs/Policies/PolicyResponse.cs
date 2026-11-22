using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Service.DTOs.Policies;

/// <summary>
/// A claimant's policy. Market and currency of a claim come from here. ReportableFrom/To are the
/// incident dates a new claim can use today (cover ∩ not future ∩ reporting window, in the market's
/// local date); both null when the policy can't take a new claim, so the UI can say why up front.
/// </summary>
public sealed record PolicyResponse(
    string PolicyNumber,
    ClaimType ProductType,
    Market Market,
    string Currency,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    bool IsActive,
    DateOnly? ReportableFrom,
    DateOnly? ReportableTo);
