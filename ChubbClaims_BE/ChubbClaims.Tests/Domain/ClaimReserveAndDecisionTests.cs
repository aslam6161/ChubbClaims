using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Events;
using ChubbClaims.Domain.Exceptions;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

/// <summary>Reserve, information requests, approval, settlement and rejection rules.</summary>
public class ClaimReserveAndDecisionTests
{
    // ------------------------------------------------------------ Reserve

    [Fact]
    public void Adjusting_the_reserve_writes_history_and_raises_old_and_new_amounts()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        claim.AdjustReserve(Officer, 18_500m, "Repair quote received", Now);

        claim.ReserveAmount.ShouldBe(18_500m);
        var row = claim.ReserveHistory.Last();
        (row.OldAmount, row.NewAmount, row.Reason, row.ActorId).ShouldBe((Estimate, 18_500m, "Repair quote received", Officer.UserId));
        var ev = claim.DomainEvents[^1].ShouldBeOfType<ReserveAdjusted>();
        (ev.OldAmount, ev.NewAmount).ShouldBe((Estimate, 18_500m));
    }

    [Fact]
    public void Reserve_cannot_go_negative()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        var eventsBefore = claim.DomainEvents.Count;

        Should.Throw<DomainValidationException>(() => claim.AdjustReserve(Officer, -1m, "Typo", Now));
        claim.ReserveAmount.ShouldBe(Estimate);
        claim.DomainEvents.Count.ShouldBe(eventsBefore);
    }

    [Fact]
    public void Reserve_can_be_set_to_zero()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        claim.AdjustReserve(Officer, 0m, "Likely no liability", Now);

        claim.OutstandingReserve.ShouldBe(0m);
    }

    [Fact]
    public void Reserve_change_needs_a_reason_and_a_different_amount()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        Should.Throw<DomainValidationException>(() => claim.AdjustReserve(Officer, 20_000m, " ", Now));
        Should.Throw<DomainValidationException>(() => claim.AdjustReserve(Officer, Estimate, "Same", Now));
    }

    // ------------------------------------------------------------ Information requests

    [Fact]
    public void Requesting_information_opens_a_request_and_waits_for_the_claimant()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        var request = claim.RequestInformation(Officer, "Please send the police report.", Now);

        claim.Status.ShouldBe(ClaimStatus.AwaitingInformation);
        claim.OpenInformationRequest.ShouldBe(request);
        var ev = claim.DomainEvents[^1].ShouldBeOfType<InformationRequested>();
        (ev.RequestId, ev.Question).ShouldBe((request.Id, "Please send the police report."));
    }

    [Fact]
    public void Only_one_information_request_can_be_open_at_a_time()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);

        Should.Throw<InvalidClaimTransitionException>(() => claim.RequestInformation(Officer, "Another?", Now));
        claim.InformationRequests.Count.ShouldBe(1);
    }

    [Fact]
    public void Answering_closes_the_request_and_returns_the_claim_to_review()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);
        var request = claim.OpenInformationRequest!;

        claim.ProvideInformation(Claimant, request.Id, "Report PR-889 attached", Now);

        request.IsOpen.ShouldBeFalse();
        request.Response.ShouldBe("Report PR-889 attached");
        claim.OpenInformationRequest.ShouldBeNull();
        claim.Status.ShouldBe(ClaimStatus.UnderReview);
        claim.DomainEvents[^1].ShouldBeOfType<InformationProvided>().RequestId.ShouldBe(request.Id);

        Should.NotThrow(() => claim.RequestInformation(Officer, "One more question", Now)); // a new one may follow
    }

    [Fact]
    public void Answer_must_target_the_open_request_and_not_be_empty()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);

        Should.Throw<DomainValidationException>(() => claim.ProvideInformation(Claimant, Guid.NewGuid(), "Hi", Now));
        Should.Throw<DomainValidationException>(() => claim.ProvideInformation(Claimant, claim.OpenInformationRequest!.Id, "", Now));
        claim.Status.ShouldBe(ClaimStatus.AwaitingInformation);
    }

    [Fact]
    public void Question_is_required()
    {
        Should.Throw<DomainValidationException>(() => InStatus(ClaimStatus.UnderReview).RequestInformation(Officer, "", Now));
    }

    // ------------------------------------------------------------ Decisions

    [Fact]
    public void Approval_sets_settlement_and_resets_reserve_to_it()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        claim.Approve(Officer, Settlement, "Agreed with workshop", Now);

        claim.SettlementAmount.ShouldBe(Settlement);
        claim.ReserveAmount.ShouldBe(Settlement);
        claim.DecisionAt.ShouldBe(Now);
        claim.Notes.ShouldHaveSingleItem().Text.ShouldBe("Agreed with workshop");
        claim.ReserveHistory.Last().NewAmount.ShouldBe(Settlement);
        var ev = claim.DomainEvents[^1].ShouldBeOfType<ClaimApproved>();
        (ev.SettlementAmount, ev.OldReserve, ev.NewReserve).ShouldBe((Settlement, Estimate, Settlement));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Settlement_amount_must_be_positive(decimal amount)
    {
        Should.Throw<DomainValidationException>(() => InStatus(ClaimStatus.UnderReview).Approve(Officer, amount, null, Now));
    }

    [Fact]
    public void Settling_pays_the_settlement_and_releases_the_outstanding_reserve()
    {
        var claim = InStatus(ClaimStatus.Approved);

        claim.Settle(Officer, Now);

        claim.IsClosed.ShouldBeTrue();
        claim.PaidAmount.ShouldBe(Settlement);
        claim.OutstandingReserve.ShouldBe(0m);
        claim.ClosedAt.ShouldBe(Now);
        var ev = claim.DomainEvents[^1].ShouldBeOfType<ClaimSettled>();
        (ev.PaidAmount, ev.ReleasedReserve).ShouldBe((Settlement, Settlement));
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_releases_the_outstanding_reserve()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        Should.Throw<DomainValidationException>(() => claim.Reject(Officer, "", Now));

        claim.Reject(Officer, "Excluded: racing event", Now);

        claim.IsClosed.ShouldBeTrue();
        claim.OutstandingReserve.ShouldBe(0m);
        claim.PaidAmount.ShouldBe(0m);
        claim.StatusHistory.Last().Reason.ShouldBe("Excluded: racing event");
        claim.DomainEvents[^1].ShouldBeOfType<ClaimRejected>().ReleasedReserve.ShouldBe(Estimate);
    }

    // ------------------------------------------------------------ Exposure consistency

    /// <summary>
    /// The exposure projection (Phase 6) adds these deltas per event. For the read model to stay
    /// correct, the deltas a claim emits must always add up to its current outstanding reserve,
    /// and to zero once it is closed. This guards the "approval changes the reserve" drift bug.
    /// </summary>
    private static decimal SumOfExposureDeltas(Claim claim) => claim.DomainEvents.Sum(e => e switch
    {
        ClaimSubmitted s => s.InitialReserve,
        ReserveAdjusted r => r.NewAmount - r.OldAmount,
        ClaimApproved a => a.NewReserve - a.OldReserve,
        ClaimSettled s => -s.ReleasedReserve,
        ClaimRejected r => -r.ReleasedReserve,
        _ => 0m,
    });

    [Fact]
    public void Event_deltas_always_equal_the_outstanding_reserve()
    {
        var settled = InStatus(ClaimStatus.UnderReview);
        settled.AdjustReserve(Officer, 15_000m, "Quote", Now);
        SumOfExposureDeltas(settled).ShouldBe(settled.OutstandingReserve);
        settled.Approve(Officer, 9_000m, null, Now);
        SumOfExposureDeltas(settled).ShouldBe(settled.OutstandingReserve);
        settled.AdjustReserve(Officer, 9_500m, "Towing added", Now);
        settled.Settle(Officer, Now);
        SumOfExposureDeltas(settled).ShouldBe(0m);

        var rejected = InStatus(ClaimStatus.AwaitingInformation);
        rejected.AdjustReserve(Officer, 7_000m, "Lower quote", Now);
        rejected.ProvideInformation(Claimant, rejected.OpenInformationRequest!.Id, "Here", Now);
        rejected.Reject(Officer, "Excluded", Now);
        SumOfExposureDeltas(rejected).ShouldBe(0m);
    }
}
