using ChubbClaims.Domain.Constants;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Domain.ValueObjects;

namespace ChubbClaims.Tests.Domain;

/// <summary>Shared actors, policies and claims in every lifecycle status.</summary>
internal static class ClaimTestData
{
    /// <summary>2026-11-21 10:00 in Kuala Lumpur.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 2, 0, 0, TimeSpan.Zero);

    public static readonly Actor Claimant = new(Guid.NewGuid(), UserRole.Claimant, null);
    public static readonly Actor OtherClaimant = new(Guid.NewGuid(), UserRole.Claimant, null);
    public static readonly Actor Officer = new(Guid.NewGuid(), UserRole.ClaimsOfficer, Market.MY);
    public static readonly Actor OtherOfficer = new(Guid.NewGuid(), UserRole.ClaimsOfficer, Market.MY);
    public static readonly Actor SgOfficer = new(Guid.NewGuid(), UserRole.ClaimsOfficer, Market.SG);
    public static readonly Actor Manager = new(Guid.NewGuid(), UserRole.ClaimsManager, Market.MY);

    public static readonly ClaimSubmissionRules Rules = new(
        DefaultReserve: 5_000m, HighPriorityThreshold: 50_000m, SlaDaysToDecision: 10, MaxIncidentAgeDays: 365);

    public const decimal Estimate = 12_000m;
    public const decimal Settlement = 10_000m;

    public static Policy Policy(
        ClaimType type = ClaimType.Motor, Market market = Market.MY, Guid? holder = null, bool active = true,
        DateOnly? from = null, DateOnly? to = null) =>
        new($"POL-{market}-{type}-001", holder ?? Claimant.UserId, market, Markets.CurrencyOf(market), type,
            from ?? new DateOnly(2026, 1, 1), to ?? new DateOnly(2026, 12, 31), active);

    public static IncidentReport MotorReport(DateOnly? incidentDate = null, decimal? estimate = Estimate) =>
        new(ClaimType.Motor, incidentDate ?? new DateOnly(2026, 11, 18), "Jalan Ampang, Kuala Lumpur",
            "Rear-ended at a traffic light", estimate, new MotorDetails("WXY 1234", true, "PR-889"), null);

    public static IncidentReport PropertyReport(decimal? estimate = Estimate) =>
        new(ClaimType.Property, new DateOnly(2026, 11, 18), "Mont Kiara", "Burst pipe flooded the kitchen",
            estimate, null, new PropertyDetails("12 Jalan Kiara, Kuala Lumpur", DamageType.Water));

    /// <summary>A photo as the service would hand it to the domain after storing it.</summary>
    public static DocumentFile Photo(string fileName = "rear-bumper.jpg", string contentType = "image/jpeg", long sizeBytes = 2_400_000) =>
        new($"claims/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg", fileName, contentType, sizeBytes, new string('a', 64));

    public static Claim Submit(IncidentReport? report = null, Policy? policy = null, Actor? claimant = null, DateTimeOffset? now = null) =>
        Claim.Submit(report ?? MotorReport(), policy ?? Policy(), claimant ?? Claimant,
            ClaimNumber.Create(Market.MY, 2026, 1), Rules, now ?? Now);

    /// <summary>A claim moved through the real lifecycle into <paramref name="status"/> (officer = <see cref="Officer"/>).</summary>
    public static Claim InStatus(ClaimStatus status)
    {
        var claim = Submit();
        var t = Now.AddHours(1);
        if (status == ClaimStatus.Submitted) return claim;

        claim.PickUp(Officer, t);
        switch (status)
        {
            case ClaimStatus.AwaitingInformation:
                claim.RequestInformation(Officer, "Please upload photos of the damage.", t);
                break;
            case ClaimStatus.Approved:
                claim.Approve(Officer, Settlement, null, t);
                break;
            case ClaimStatus.Settled:
                claim.Approve(Officer, Settlement, null, t);
                claim.Settle(Officer, t);
                break;
            case ClaimStatus.Rejected:
                claim.Reject(Officer, "Excluded: racing event", t);
                break;
        }
        return claim;
    }
}
