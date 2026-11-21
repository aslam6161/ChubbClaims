using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.Settings;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Interfaces;
using ChubbClaims.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Services;

/// <summary>ClaimService orchestration and resource-level visibility, with repositories faked.</summary>
public class ClaimServiceTests
{
    private readonly IClaimRepository _claims = Substitute.For<IClaimRepository>();
    private readonly IPolicyRepository _policies = Substitute.For<IPolicyRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly ClaimRulesSettings Rules = new()
    {
        SlaDaysToDecision = 10,
        MaxIncidentAgeDays = 365,
        Markets = { [Market.MY] = new() { DefaultMotorReserve = 5_000m, DefaultPropertyReserve = 20_000m, HighPriorityThreshold = 50_000m } },
    };

    private ClaimService ServiceFor(Actor caller) => new(
        _claims, _policies, _unitOfWork, new TestCurrentUser(caller), Rules, new FixedTimeProvider(Now), NullLogger<ClaimService>.Instance);

    private static SubmitClaimRequest MotorRequest(decimal? estimate = 7_500m) => new(
        "POL-MY-Motor-001", ClaimType.Motor, new DateOnly(2026, 11, 20), "Jalan Ampang", "Rear-ended", estimate,
        new MotorDetailsDto("WXY 1234", false, null), null);

    // ------------------------------------------------------------ FNOL

    [Fact]
    public async Task Submit_on_an_unknown_policy_is_not_found_and_saves_nothing()
    {
        _policies.GetAsync("POL-MY-Motor-001", Arg.Any<CancellationToken>()).Returns((Policy?)null);

        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(Claimant).SubmitAsync(MotorRequest(), CancellationToken.None));

        _claims.DidNotReceive().Add(Arg.Any<Claim>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_on_someone_elses_policy_looks_exactly_like_a_missing_policy()
    {
        _policies.GetAsync("POL-MY-Motor-001", Arg.Any<CancellationToken>()).Returns(Policy(holder: OtherClaimant.UserId));

        var ex = await Should.ThrowAsync<NotFoundException>(() => ServiceFor(Claimant).SubmitAsync(MotorRequest(), CancellationToken.None));

        ex.Message.ShouldBe("Policy POL-MY-Motor-001 was not found.");
        await _claims.DidNotReceive().NextClaimSequenceAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Valid_submission_numbers_the_claim_and_saves_it_once()
    {
        _policies.GetAsync("POL-MY-Motor-001", Arg.Any<CancellationToken>()).Returns(Policy());
        _claims.NextClaimSequenceAsync(Arg.Any<CancellationToken>()).Returns(42L);

        var response = await ServiceFor(Claimant).SubmitAsync(MotorRequest(), CancellationToken.None);

        response.ClaimNumber.ShouldBe("CLM-MY-2026-000042");
        response.Status.ShouldBe(ClaimStatus.Submitted);
        _claims.Received(1).Add(Arg.Is<Claim>(c =>
            c.Id == response.Id && c.ClaimantId == Claimant.UserId && c.Currency == "MYR" && c.ReserveAmount == 7_500m));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_an_estimate_the_market_default_reserve_for_the_claim_type_is_used()
    {
        _policies.GetAsync("POL-MY-Motor-001", Arg.Any<CancellationToken>()).Returns(Policy());
        _claims.NextClaimSequenceAsync(Arg.Any<CancellationToken>()).Returns(1L);

        await ServiceFor(Claimant).SubmitAsync(MotorRequest(estimate: null), CancellationToken.None);

        _claims.Received(1).Add(Arg.Is<Claim>(c => c.ReserveAmount == 5_000m));
    }

    // ------------------------------------------------------------ Visibility

    private Claim StoredClaim(ClaimStatus status = ClaimStatus.Submitted)
    {
        var claim = InStatus(status);
        _claims.GetAsync(claim.Id, Arg.Any<CancellationToken>()).Returns(claim);
        return claim;
    }

    [Fact]
    public async Task Owner_sees_the_claim_without_internal_fields()
    {
        var claim = StoredClaim();

        var detail = await ServiceFor(Claimant).GetAsync(claim.Id, CancellationToken.None);

        detail.ClaimNumber.ShouldBe(claim.ClaimNumber);
        detail.ReserveAmount.ShouldBeNull();
        detail.OutstandingReserve.ShouldBeNull();
        detail.Notes.ShouldBeNull();
        detail.ReserveHistory.ShouldBeNull();
    }

    [Fact]
    public async Task Staff_in_the_claim_market_see_internal_fields()
    {
        var claim = StoredClaim(ClaimStatus.UnderReview);

        var detail = await ServiceFor(OtherOfficer).GetAsync(claim.Id, CancellationToken.None);

        detail.ReserveAmount.ShouldBe(Estimate);
        detail.AssignedOfficerId.ShouldBe(Officer.UserId);
        detail.Notes.ShouldNotBeNull();
    }

    [Fact]
    public async Task Another_claimant_staff_from_another_market_and_a_missing_claim_all_get_404()
    {
        var claim = StoredClaim();

        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(OtherClaimant).GetAsync(claim.Id, CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(SgOfficer).GetAsync(claim.Id, CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(Claimant).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // ------------------------------------------------------------ Information response

    [Fact]
    public async Task Owner_answer_moves_the_claim_back_to_review_and_saves()
    {
        var claim = StoredClaim(ClaimStatus.AwaitingInformation);
        var requestId = claim.OpenInformationRequest!.Id;

        await ServiceFor(Claimant).RespondToInformationRequestAsync(
            claim.Id, requestId, new InformationResponseRequest("Photos uploaded"), CancellationToken.None);

        claim.Status.ShouldBe(ClaimStatus.UnderReview);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Another_claimant_cannot_answer_and_learns_nothing()
    {
        var claim = StoredClaim(ClaimStatus.AwaitingInformation);

        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(OtherClaimant).RespondToInformationRequestAsync(
            claim.Id, claim.OpenInformationRequest!.Id, new InformationResponseRequest("x"), CancellationToken.None));

        claim.Status.ShouldBe(ClaimStatus.AwaitingInformation);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------ Officer actions

    [Fact]
    public async Task Officer_action_runs_the_domain_method_and_saves_once()
    {
        var claim = StoredClaim(ClaimStatus.UnderReview);

        await ServiceFor(Officer).ApproveAsync(claim.Id, new ApproveClaimRequest(9_000m, "Agreed"), CancellationToken.None);

        claim.Status.ShouldBe(ClaimStatus.Approved);
        claim.SettlementAmount.ShouldBe(9_000m);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Officer_from_another_market_gets_404_before_any_domain_rule()
    {
        var claim = StoredClaim();

        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(SgOfficer).PickUpAsync(claim.Id, CancellationToken.None));

        claim.AssignedOfficerId.ShouldBeNull();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_market_officer_who_is_not_assigned_is_refused_by_the_aggregate()
    {
        var claim = StoredClaim(ClaimStatus.UnderReview);

        await Should.ThrowAsync<ClaimAccessDeniedException>(() =>
            ServiceFor(OtherOfficer).RejectAsync(claim.Id, new RejectClaimRequest("No"), CancellationToken.None));

        claim.Status.ShouldBe(ClaimStatus.UnderReview);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pick_up_assigns_the_caller()
    {
        var claim = StoredClaim();

        await ServiceFor(Manager).PickUpAsync(claim.Id, CancellationToken.None);

        claim.AssignedOfficerId.ShouldBe(Manager.UserId);
    }
}
