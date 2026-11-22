using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.Events;

/// <summary>Base for claim events. Each event carries what consumers need, so they never call back into the API.</summary>
public abstract record ClaimEvent(Guid ClaimId, DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
}

public sealed record ClaimSubmitted(Guid ClaimId, DateTimeOffset OccurredAt, Guid ClaimantId, decimal InitialReserve, DateTimeOffset SlaDueAt)
    : ClaimEvent(ClaimId, OccurredAt);

public sealed record ClaimPickedUp(Guid ClaimId, DateTimeOffset OccurredAt, Guid OfficerId)
    : ClaimEvent(ClaimId, OccurredAt);

public sealed record ReserveAdjusted(Guid ClaimId, DateTimeOffset OccurredAt, decimal OldAmount, decimal NewAmount, string Reason)
    : ClaimEvent(ClaimId, OccurredAt);

public sealed record InformationRequested(Guid ClaimId, DateTimeOffset OccurredAt, Guid RequestId, string Question)
    : ClaimEvent(ClaimId, OccurredAt);

/// <summary>AssignedOfficerId: who asked, so they can be told the answer arrived.</summary>
public sealed record InformationProvided(Guid ClaimId, DateTimeOffset OccurredAt, Guid RequestId, Guid? AssignedOfficerId)
    : ClaimEvent(ClaimId, OccurredAt);

/// <summary>Approval resets the reserve to the settlement amount; old/new let the exposure projection apply the delta.</summary>
public sealed record ClaimApproved(Guid ClaimId, DateTimeOffset OccurredAt, decimal SettlementAmount, decimal OldReserve, decimal NewReserve)
    : ClaimEvent(ClaimId, OccurredAt);

/// <summary>ReleasedReserve = outstanding reserve immediately before closing.</summary>
public sealed record ClaimSettled(Guid ClaimId, DateTimeOffset OccurredAt, decimal PaidAmount, decimal ReleasedReserve)
    : ClaimEvent(ClaimId, OccurredAt);

/// <summary>ReleasedReserve = outstanding reserve immediately before closing.</summary>
public sealed record ClaimRejected(Guid ClaimId, DateTimeOffset OccurredAt, string Reason, decimal ReleasedReserve)
    : ClaimEvent(ClaimId, OccurredAt);

/// <summary>
/// A photo or document was attached. UploadedByRole and AssignedOfficerId let the notifier tell the
/// other side (claimant uploads → assigned officer; staff uploads → claimant) without a lookup.
/// </summary>
public sealed record DocumentAttached(
    Guid ClaimId, DateTimeOffset OccurredAt, Guid DocumentId, string FileName, string ContentType,
    Guid UploadedById, UserRole UploadedByRole, Guid? AssignedOfficerId)
    : ClaimEvent(ClaimId, OccurredAt);
