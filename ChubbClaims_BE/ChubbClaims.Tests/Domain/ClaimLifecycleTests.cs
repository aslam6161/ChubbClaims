using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Events;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.Rules;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

/// <summary>The state machine, tested over every status × action combination (6 × 7 = 42 cases).</summary>
public class ClaimLifecycleTests
{
    // The lifecycle from BACKEND.md §5.2, restated independently of ClaimTransitions.
    private static readonly Dictionary<(ClaimStatus, ClaimAction), ClaimStatus> AllowedTransitions = new()
    {
        [(ClaimStatus.Submitted, ClaimAction.PickUp)] = ClaimStatus.UnderReview,
        [(ClaimStatus.UnderReview, ClaimAction.RequestInformation)] = ClaimStatus.AwaitingInformation,
        [(ClaimStatus.AwaitingInformation, ClaimAction.ProvideInformation)] = ClaimStatus.UnderReview,
        [(ClaimStatus.UnderReview, ClaimAction.AdjustReserve)] = ClaimStatus.UnderReview,
        [(ClaimStatus.AwaitingInformation, ClaimAction.AdjustReserve)] = ClaimStatus.AwaitingInformation,
        [(ClaimStatus.Approved, ClaimAction.AdjustReserve)] = ClaimStatus.Approved,
        [(ClaimStatus.UnderReview, ClaimAction.Approve)] = ClaimStatus.Approved,
        [(ClaimStatus.Approved, ClaimAction.Settle)] = ClaimStatus.Settled,
        [(ClaimStatus.UnderReview, ClaimAction.Reject)] = ClaimStatus.Rejected,
    };

    private static readonly Dictionary<ClaimAction, Type> EventFor = new()
    {
        [ClaimAction.PickUp] = typeof(ClaimPickedUp),
        [ClaimAction.RequestInformation] = typeof(InformationRequested),
        [ClaimAction.ProvideInformation] = typeof(InformationProvided),
        [ClaimAction.AdjustReserve] = typeof(ReserveAdjusted),
        [ClaimAction.Approve] = typeof(ClaimApproved),
        [ClaimAction.Settle] = typeof(ClaimSettled),
        [ClaimAction.Reject] = typeof(ClaimRejected),
    };

    public static TheoryData<ClaimStatus, ClaimAction> EveryStatusAndAction()
    {
        var data = new TheoryData<ClaimStatus, ClaimAction>();
        foreach (var status in Enum.GetValues<ClaimStatus>())
        foreach (var action in Enum.GetValues<ClaimAction>())
            data.Add(status, action);
        return data;
    }

    /// <summary>Performs the action with the right actor and valid input, so only the transition rule decides.</summary>
    private static void Perform(Claim claim, ClaimAction action)
    {
        var t = Now.AddDays(1);
        switch (action)
        {
            case ClaimAction.PickUp: claim.PickUp(Officer, t); break;
            case ClaimAction.RequestInformation: claim.RequestInformation(Officer, "Repair quote?", t); break;
            case ClaimAction.ProvideInformation:
                claim.ProvideInformation(Claimant, claim.OpenInformationRequest?.Id ?? Guid.NewGuid(), "Attached.", t); break;
            case ClaimAction.AdjustReserve: claim.AdjustReserve(Officer, claim.ReserveAmount + 500m, "Repair quote received", t); break;
            case ClaimAction.Approve: claim.Approve(Officer, Settlement, null, t); break;
            case ClaimAction.Settle: claim.Settle(Officer, t); break;
            case ClaimAction.Reject: claim.Reject(Officer, "Excluded", t); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatusAndAction))]
    public void Every_status_and_action_follows_the_lifecycle(ClaimStatus from, ClaimAction action)
    {
        var claim = InStatus(from);
        var eventsBefore = claim.DomainEvents.Count;

        if (AllowedTransitions.TryGetValue((from, action), out var expectedStatus))
        {
            Perform(claim, action);

            claim.Status.ShouldBe(expectedStatus);
            claim.DomainEvents.Count.ShouldBe(eventsBefore + 1);
            claim.DomainEvents[^1].ShouldBeOfType(EventFor[action]);
        }
        else
        {
            var ex = Should.Throw<InvalidClaimTransitionException>(() => Perform(claim, action));

            ex.From.ShouldBe(from);
            ex.Action.ShouldBe(action);
            claim.Status.ShouldBe(from);
            claim.DomainEvents.Count.ShouldBe(eventsBefore);
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatusAndAction))]
    public void Transition_table_matches_the_specified_lifecycle(ClaimStatus from, ClaimAction action)
    {
        ClaimTransitions.IsAllowed(from, action).ShouldBe(AllowedTransitions.ContainsKey((from, action)));
    }

    [Fact]
    public void Submission_and_every_status_change_write_an_audit_row()
    {
        var claim = InStatus(ClaimStatus.Settled);

        claim.StatusHistory.Select(h => (h.From, h.To)).ShouldBe(
        [
            (null, ClaimStatus.Submitted),
            (ClaimStatus.Submitted, ClaimStatus.UnderReview),
            (ClaimStatus.UnderReview, ClaimStatus.Approved),
            (ClaimStatus.Approved, ClaimStatus.Settled),
        ]);
        claim.StatusHistory.First().ActorId.ShouldBe(Claimant.UserId);
        claim.StatusHistory.Skip(1).ShouldAllBe(h => h.ActorId == Officer.UserId && h.ActorRole == UserRole.ClaimsOfficer);
    }

    [Fact]
    public void Reserve_adjustment_does_not_write_a_status_row()
    {
        var claim = InStatus(ClaimStatus.UnderReview);
        var rowsBefore = claim.StatusHistory.Count;

        claim.AdjustReserve(Officer, 15_000m, "Repair quote received", Now);

        claim.StatusHistory.Count.ShouldBe(rowsBefore);
    }

    [Fact]
    public void Allowed_actions_per_status_are_exposed_for_the_UI()
    {
        ClaimTransitions.AllowedFrom(ClaimStatus.UnderReview).ShouldBe(
            [ClaimAction.RequestInformation, ClaimAction.AdjustReserve, ClaimAction.Approve, ClaimAction.Reject],
            ignoreOrder: true);
        ClaimTransitions.AllowedFrom(ClaimStatus.Settled).ShouldBeEmpty();
        ClaimTransitions.AllowedFrom(ClaimStatus.Rejected).ShouldBeEmpty();
    }
}
