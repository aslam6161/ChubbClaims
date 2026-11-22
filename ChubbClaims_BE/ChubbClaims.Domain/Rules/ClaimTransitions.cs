using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;

namespace ChubbClaims.Domain.Rules;

/// <summary>
/// The claim lifecycle as data: which action is allowed from which status, and where it leads.
/// Anything not in the table is an invalid transition (409).
/// <code>
/// Submitted -PickUp-> UnderReview -RequestInformation-> AwaitingInformation
///                     UnderReview <-ProvideInformation- AwaitingInformation
///                     UnderReview -Approve-> Approved -Settle-> Settled (closed)
///                     UnderReview -Reject--> Rejected                    (closed)
/// AdjustReserve: allowed in UnderReview, AwaitingInformation, Approved; status unchanged.
/// AttachDocument: allowed while the claim is open (not Settled or Rejected); status unchanged.
/// </code>
/// </summary>
public static class ClaimTransitions
{
    private static readonly IReadOnlyDictionary<(ClaimStatus From, ClaimAction Action), ClaimStatus> Table =
        new Dictionary<(ClaimStatus, ClaimAction), ClaimStatus>
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
            [(ClaimStatus.Submitted, ClaimAction.AttachDocument)] = ClaimStatus.Submitted,
            [(ClaimStatus.UnderReview, ClaimAction.AttachDocument)] = ClaimStatus.UnderReview,
            [(ClaimStatus.AwaitingInformation, ClaimAction.AttachDocument)] = ClaimStatus.AwaitingInformation,
            [(ClaimStatus.Approved, ClaimAction.AttachDocument)] = ClaimStatus.Approved,
        };

    public static bool IsAllowed(ClaimStatus from, ClaimAction action) => Table.ContainsKey((from, action));

    /// <summary>The status after <paramref name="action"/>, or throws <see cref="InvalidClaimTransitionException"/>.</summary>
    public static ClaimStatus Next(ClaimStatus from, ClaimAction action) =>
        Table.TryGetValue((from, action), out var to) ? to : throw new InvalidClaimTransitionException(from, action);

    /// <summary>Actions allowed from a status (lets the API tell the UI which buttons to offer).</summary>
    public static IReadOnlyList<ClaimAction> AllowedFrom(ClaimStatus from) =>
        Table.Keys.Where(k => k.From == from).Select(k => k.Action).ToList();
}
