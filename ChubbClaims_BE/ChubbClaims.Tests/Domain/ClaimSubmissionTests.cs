using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Events;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.ValueObjects;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

/// <summary>FNOL business rules (BACKEND.md §5.3).</summary>
public class ClaimSubmissionTests
{
    [Fact]
    public void Valid_motor_claim_is_submitted_with_policy_market_currency_and_estimate_as_reserve()
    {
        var claim = Submit();

        claim.Status.ShouldBe(ClaimStatus.Submitted);
        claim.ClaimNumber.ShouldBe("CLM-MY-2026-000001");
        claim.Market.ShouldBe(Market.MY);
        claim.Currency.ShouldBe("MYR");
        claim.ClaimantId.ShouldBe(Claimant.UserId);
        claim.AssignedOfficerId.ShouldBeNull();
        claim.ReserveAmount.ShouldBe(Estimate);
        claim.OutstandingReserve.ShouldBe(Estimate);
        claim.Priority.ShouldBe(Priority.Normal);
        claim.SlaDueAt.ShouldBe(Now.AddDays(Rules.SlaDaysToDecision));

        var submitted = claim.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ClaimSubmitted>();
        submitted.ClaimId.ShouldBe(claim.Id);
        submitted.InitialReserve.ShouldBe(Estimate);
        submitted.SlaDueAt.ShouldBe(claim.SlaDueAt);
        claim.ReserveHistory.ShouldHaveSingleItem().NewAmount.ShouldBe(Estimate);
    }

    [Fact]
    public void Currency_comes_from_the_policy_market()
    {
        var claim = Submit(policy: Policy(market: Market.SG));

        claim.Market.ShouldBe(Market.SG);
        claim.Currency.ShouldBe("SGD");
    }

    [Fact]
    public void Without_an_estimate_the_configured_default_reserve_is_used()
    {
        var claim = Submit(MotorReport(estimate: null));

        claim.ReserveAmount.ShouldBe(Rules.DefaultReserve);
        claim.DomainEvents.OfType<ClaimSubmitted>().Single().InitialReserve.ShouldBe(Rules.DefaultReserve);
    }

    [Fact]
    public void Estimate_above_the_market_threshold_is_high_priority()
    {
        Submit(MotorReport(estimate: Rules.HighPriorityThreshold + 1)).Priority.ShouldBe(Priority.High);
        Submit(MotorReport(estimate: Rules.HighPriorityThreshold)).Priority.ShouldBe(Priority.Normal);
    }

    [Fact]
    public void Valid_property_claim_is_submitted()
    {
        var claim = Submit(PropertyReport(), Policy(ClaimType.Property));

        claim.Type.ShouldBe(ClaimType.Property);
        claim.PropertyDetails!.DamageType.ShouldBe(DamageType.Water);
    }

    [Fact]
    public void Expired_policy_is_rejected()
    {
        var expired = Policy(from: new DateOnly(2025, 1, 1), to: new DateOnly(2025, 12, 31));

        Should.Throw<DomainValidationException>(() => Submit(policy: expired)).Message.ShouldContain("not in force");
    }

    [Fact]
    public void Inactive_policy_is_rejected()
    {
        Should.Throw<DomainValidationException>(() => Submit(policy: Policy(active: false))).Message.ShouldContain("not active");
    }

    [Fact]
    public void Claim_on_another_users_policy_is_denied()
    {
        Should.Throw<ClaimAccessDeniedException>(() => Submit(policy: Policy(holder: OtherClaimant.UserId)));
    }

    [Fact]
    public void Staff_cannot_report_an_incident()
    {
        Should.Throw<ClaimAccessDeniedException>(() => Submit(claimant: Officer));
    }

    [Fact]
    public void Future_incident_date_is_rejected()
    {
        Should.Throw<DomainValidationException>(() => Submit(MotorReport(new DateOnly(2026, 11, 22))))
            .Message.ShouldContain("future");
    }

    [Fact]
    public void Incident_older_than_the_reporting_window_is_rejected()
    {
        var tooOld = new DateOnly(2026, 11, 21).AddDays(-Rules.MaxIncidentAgeDays - 1);
        var policy = Policy(from: new DateOnly(2025, 1, 1));

        Should.Throw<DomainValidationException>(() => Submit(MotorReport(tooOld), policy)).Message.ShouldContain("365 days");
    }

    [Fact]
    public void Incident_date_is_checked_against_the_market_local_date_not_utc()
    {
        // 22:00 UTC on 20 Nov is 09:00 on 21 Nov in Sydney: a same-day claim there is not "in the future".
        var sydneyMorning = new DateTimeOffset(2026, 11, 20, 22, 0, 0, TimeSpan.Zero);
        var auPolicy = Policy(market: Market.AU);

        Should.NotThrow(() => Submit(MotorReport(new DateOnly(2026, 11, 21)), auPolicy, now: sydneyMorning));

        // 15:00 UTC on 20 Nov is still 23:00 on 20 Nov in Kuala Lumpur: 21 Nov is in the future there.
        var klLateEvening = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);
        Should.Throw<DomainValidationException>(() => Submit(MotorReport(new DateOnly(2026, 11, 21)), now: klLateEvening));
    }

    [Fact]
    public void Claim_type_must_match_the_policy_product()
    {
        Should.Throw<DomainValidationException>(() => Submit(PropertyReport(), Policy(ClaimType.Motor)))
            .Message.ShouldContain("covers Motor");
    }

    [Fact]
    public void Motor_claim_needs_vehicle_details()
    {
        var report = MotorReport() with { MotorDetails = null };

        Should.Throw<DomainValidationException>(() => Submit(report)).Message.ShouldContain("vehicle details");
    }

    [Fact]
    public void Motor_claim_cannot_carry_property_details()
    {
        var report = MotorReport() with { PropertyDetails = new PropertyDetails("1 Jalan X", DamageType.Fire) };

        Should.Throw<DomainValidationException>(() => Submit(report));
    }

    [Fact]
    public void Property_claim_needs_property_details()
    {
        var report = PropertyReport() with { PropertyDetails = null };

        Should.Throw<DomainValidationException>(() => Submit(report, Policy(ClaimType.Property)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Estimate_when_given_must_be_positive(decimal estimate)
    {
        Should.Throw<DomainValidationException>(() => Submit(MotorReport(estimate: estimate)));
    }

    [Fact]
    public void Description_and_location_are_required()
    {
        Should.Throw<DomainValidationException>(() => Submit(MotorReport() with { Description = "  " }));
        Should.Throw<DomainValidationException>(() => Submit(MotorReport() with { IncidentLocation = "" }));
    }

    [Fact]
    public void Claim_number_has_market_year_and_zero_padded_sequence()
    {
        ClaimNumber.Create(Market.TH, 2026, 123).Value.ShouldBe("CLM-TH-2026-000123");
        Should.Throw<ArgumentOutOfRangeException>(() => ClaimNumber.Create(Market.TH, 2026, 0));
    }
}
