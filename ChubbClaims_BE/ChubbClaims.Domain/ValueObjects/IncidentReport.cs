using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.ValueObjects;

/// <summary>What the claimant reports at first notice of loss (FNOL). Validated by <c>Claim.Submit</c>.</summary>
public sealed record IncidentReport(
    ClaimType Type,
    DateOnly IncidentDate,
    string IncidentLocation,
    string Description,
    decimal? EstimatedLossAmount,
    MotorDetails? MotorDetails,
    PropertyDetails? PropertyDetails);
