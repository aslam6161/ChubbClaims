using System.Globalization;
using ChubbClaims.Domain.ReadModels;
using ChubbClaims.Messaging.Contracts;

namespace ChubbClaims.Messaging.Projections;

/// <summary>
/// Who is told what for each claim event (pure, unit tested). Claimants hear about their claim's
/// progress; the assigned officer hears when the claimant answers. Reserve changes are internal
/// and never produce a claimant notification.
/// </summary>
public static class NotificationProjection
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static IReadOnlyList<Notification> For(EventEnvelope e)
    {
        var n = e.ClaimNumber;

        if (e.EventType == ClaimEventTypes.InformationProvided)
        {
            var officerId = e.DataAs<InformationProvidedData>().AssignedOfficerId;
            return officerId is { } id
                ? [new Notification(id, e.ClaimId, n, $"The claimant answered your question on claim {n}.", e.OccurredAt)]
                : [];
        }

        var claimantMessage = e.EventType switch
        {
            ClaimEventTypes.ClaimSubmitted =>
                $"We received your claim {n}. We aim to reach a decision by {e.DataAs<ClaimSubmittedData>().SlaDueAt.ToString("d MMM yyyy", Invariant)}.",
            ClaimEventTypes.ClaimPickedUp => $"Your claim {n} is now being reviewed by a claims officer.",
            ClaimEventTypes.InformationRequested => $"We need more information for claim {n}: {e.DataAs<InformationRequestedData>().Question}",
            ClaimEventTypes.ClaimApproved =>
                $"Your claim {n} was approved for {e.Currency} {e.DataAs<ClaimApprovedData>().SettlementAmount.ToString("N2", Invariant)}.",
            ClaimEventTypes.ClaimSettled =>
                $"Payment of {e.Currency} {e.DataAs<ClaimSettledData>().PaidAmount.ToString("N2", Invariant)} for claim {n} has been made.",
            ClaimEventTypes.ClaimRejected => $"Your claim {n} was not approved: {e.DataAs<ClaimRejectedData>().Reason}",
            _ => null, // ReserveAdjusted (internal) and unknown future types
        };

        return claimantMessage is not null && e.ClaimantId is { } claimantId
            ? [new Notification(claimantId, e.ClaimId, n, claimantMessage, e.OccurredAt)]
            : [];
    }
}
