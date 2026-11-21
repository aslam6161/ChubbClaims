using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.Rules;

/// <summary>Who has to act next on a claim.</summary>
public enum WaitingOn
{
    Officer = 1,
    Claimant = 2,
    Nobody = 3, // closed
}

/// <summary>Workload rules used by the work queue and team views.</summary>
public static class ClaimProgress
{
    public static WaitingOn WaitingOnFor(ClaimStatus status) => status switch
    {
        ClaimStatus.Submitted or ClaimStatus.UnderReview or ClaimStatus.Approved => WaitingOn.Officer,
        ClaimStatus.AwaitingInformation => WaitingOn.Claimant,
        ClaimStatus.Settled or ClaimStatus.Rejected => WaitingOn.Nobody,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>
    /// The SLA is "time to decision": a claim is overdue once its SLA date has passed without an
    /// approve/reject decision. Approved claims awaiting payment are not overdue.
    /// (The SLA clock keeps running while waiting on the claimant; pausing it is a "more time" item.)
    /// </summary>
    public static bool IsOverdue(DateTimeOffset slaDueAt, DateTimeOffset? decisionAt, DateTimeOffset now) =>
        decisionAt is null && slaDueAt < now;
}
