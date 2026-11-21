using ChubbClaims.Messaging.Contracts;
using ChubbClaims.Repository.Interfaces;

namespace ChubbClaims.Messaging.Projections;

/// <summary>
/// Pure delta rules for the exposure read model (BACKEND.md §7). Deltas, not absolute values,
/// so the projector never reads a claim; that is only safe because each event is applied once
/// (see <see cref="IExposureRepository.ApplyOnceAsync"/>).
/// </summary>
public static class ExposureProjection
{
    /// <summary>The change this event makes to exposure, or null if it does not affect it.</summary>
    public static ExposureDelta? DeltaFor(EventEnvelope e)
    {
        switch (e.EventType)
        {
            case ClaimEventTypes.ClaimSubmitted:
                return new ExposureDelta(+1, e.DataAs<ClaimSubmittedData>().InitialReserve);

            case ClaimEventTypes.ReserveAdjusted:
            {
                var d = e.DataAs<ReserveAdjustedData>();
                return new ExposureDelta(0, d.NewAmount - d.OldAmount);
            }

            case ClaimEventTypes.ClaimApproved: // approval resets the reserve to the settlement amount
            {
                var d = e.DataAs<ClaimApprovedData>();
                return new ExposureDelta(0, d.NewReserve - d.OldReserve);
            }

            case ClaimEventTypes.ClaimSettled:
                return new ExposureDelta(-1, -e.DataAs<ClaimSettledData>().ReleasedReserve);

            case ClaimEventTypes.ClaimRejected:
                return new ExposureDelta(-1, -e.DataAs<ClaimRejectedData>().ReleasedReserve);

            default:
                return null; // picked up, information requested/provided: no money moves
        }
    }
}
