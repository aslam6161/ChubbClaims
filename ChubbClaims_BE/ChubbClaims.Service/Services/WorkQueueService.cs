using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.WorkQueue;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Service.Services;

/// <summary>
/// The officer's work queue. A plain indexed query on the Claims table: it is a filtered,
/// per-officer read on a few columns, so it does not need a separate read model (unlike exposure).
/// </summary>
public sealed class WorkQueueService(IClaimRepository claims, ICurrentUser currentUser, TimeProvider clock) : IWorkQueueService
{
    /// <summary>Hard cap per request; paging is a "more time" item.</summary>
    public const int MaxRows = 200;

    public async Task<IReadOnlyList<WorkQueueItemResponse>> GetAsync(
        WorkQueueView view, ClaimStatus? status, WorkQueueSort sort, CancellationToken ct)
    {
        var market = currentUser.Market ?? throw new ForbiddenException("Only claims staff have a work queue.");

        var rows = await claims.GetWorkQueueAsync(new WorkQueueFilter(
            market,
            AssignedTo: view == WorkQueueView.Mine ? currentUser.UserId : null,
            status,
            sort == WorkQueueSort.Reported ? WorkQueueOrder.ReportedAt : WorkQueueOrder.SlaDueAt,
            MaxRows), ct);

        var now = clock.GetUtcNow();
        return rows.Select(r => new WorkQueueItemResponse(
            r.Id, r.ClaimNumber, r.Type, r.Market, r.Status, r.Priority, r.ReportedAt,
            AgeDays: (int)(now - r.ReportedAt).TotalDays,
            r.SlaDueAt,
            IsOverdue: ClaimProgress.IsOverdue(r.SlaDueAt, r.DecisionAt, now),
            WaitingOn: ClaimProgress.WaitingOnFor(r.Status),
            r.AssignedOfficerId, r.OutstandingReserve, r.Currency)).ToList();
    }
}
