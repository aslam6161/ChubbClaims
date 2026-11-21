using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.ValueObjects;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

/// <summary>Who may act on a claim. Enforced in the aggregate, so no endpoint can forget it.</summary>
public class ClaimAuthorizationTests
{
    [Fact]
    public void Officer_in_the_claim_market_picks_up_and_is_assigned()
    {
        var claim = InStatus(ClaimStatus.Submitted);

        claim.PickUp(Officer, Now);

        claim.AssignedOfficerId.ShouldBe(Officer.UserId);
    }

    [Fact]
    public void Manager_in_the_claim_market_can_pick_up()
    {
        var claim = InStatus(ClaimStatus.Submitted);

        claim.PickUp(Manager, Now);

        claim.AssignedOfficerId.ShouldBe(Manager.UserId);
    }

    [Fact]
    public void Officer_from_another_market_cannot_pick_up()
    {
        var claim = InStatus(ClaimStatus.Submitted);

        Should.Throw<ClaimAccessDeniedException>(() => claim.PickUp(SgOfficer, Now));
        claim.Status.ShouldBe(ClaimStatus.Submitted);
        claim.AssignedOfficerId.ShouldBeNull();
    }

    [Fact]
    public void Claimant_cannot_pick_up()
    {
        Should.Throw<ClaimAccessDeniedException>(() => InStatus(ClaimStatus.Submitted).PickUp(Claimant, Now));
    }

    [Fact]
    public void Second_pick_up_is_a_conflict_not_a_permission_error()
    {
        var claim = InStatus(ClaimStatus.Submitted);
        claim.PickUp(Officer, Now);

        // Another officer clicked "Pick up" on a stale screen: 409 so the UI can say "already picked up".
        Should.Throw<InvalidClaimTransitionException>(() => claim.PickUp(OtherOfficer, Now));
        claim.AssignedOfficerId.ShouldBe(Officer.UserId);
    }

    public static TheoryData<ClaimStatus, string> AssignedOfficerOnlyActions() => new()
    {
        { ClaimStatus.UnderReview, "request-info" },
        { ClaimStatus.UnderReview, "adjust-reserve" },
        { ClaimStatus.UnderReview, "approve" },
        { ClaimStatus.UnderReview, "reject" },
        { ClaimStatus.Approved, "settle" },
    };

    private static void Act(Claim claim, string action, Actor actor)
    {
        switch (action)
        {
            case "request-info": claim.RequestInformation(actor, "Photos?", Now); break;
            case "adjust-reserve": claim.AdjustReserve(actor, 20_000m, "New quote", Now); break;
            case "approve": claim.Approve(actor, Settlement, null, Now); break;
            case "reject": claim.Reject(actor, "Excluded", Now); break;
            case "settle": claim.Settle(actor, Now); break;
        }
    }

    [Theory]
    [MemberData(nameof(AssignedOfficerOnlyActions))]
    public void Only_the_assigned_officer_can_act(ClaimStatus status, string action)
    {
        foreach (var notAssigned in new[] { OtherOfficer, Manager, SgOfficer, Claimant })
        {
            var claim = InStatus(status);
            var eventsBefore = claim.DomainEvents.Count;

            Should.Throw<ClaimAccessDeniedException>(() => Act(claim, action, notAssigned));
            claim.Status.ShouldBe(status);
            claim.DomainEvents.Count.ShouldBe(eventsBefore);
        }

        Should.NotThrow(() => Act(InStatus(status), action, Officer));
    }

    [Fact]
    public void Only_the_owning_claimant_can_provide_information()
    {
        var claim = InStatus(ClaimStatus.AwaitingInformation);
        var requestId = claim.OpenInformationRequest!.Id;

        Should.Throw<ClaimAccessDeniedException>(() => claim.ProvideInformation(OtherClaimant, requestId, "Hi", Now));
        Should.Throw<ClaimAccessDeniedException>(() => claim.ProvideInformation(Officer, requestId, "Hi", Now));
        claim.Status.ShouldBe(ClaimStatus.AwaitingInformation);

        claim.ProvideInformation(Claimant, requestId, "Photos attached", Now);
        claim.Status.ShouldBe(ClaimStatus.UnderReview);
    }

    [Fact]
    public void Notes_can_be_added_by_any_staff_in_the_market_only()
    {
        var claim = InStatus(ClaimStatus.UnderReview);

        claim.AddNote(OtherOfficer, "Called the workshop", Now);
        claim.AddNote(Manager, "Watch this one", Now);
        Should.Throw<ClaimAccessDeniedException>(() => claim.AddNote(SgOfficer, "x", Now));
        Should.Throw<ClaimAccessDeniedException>(() => claim.AddNote(Claimant, "x", Now));

        claim.Notes.Count.ShouldBe(2);
    }
}
