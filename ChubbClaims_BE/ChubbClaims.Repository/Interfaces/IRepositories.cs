using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.ReadModels;
using ChubbClaims.Repository.Outbox;

namespace ChubbClaims.Repository.Interfaces;

public interface IClaimRepository
{
    /// <summary>The full aggregate (with children), tracked for changes. Null if it does not exist.</summary>
    Task<Claim?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// One page of the claimant's claims, newest first, without children (read-only), plus the
    /// count of their claims waiting on them (over all their claims, not just the search or page).
    /// </summary>
    Task<ClaimantClaimsPage> GetByClaimantAsync(ClaimantClaimsFilter filter, CancellationToken ct);

    /// <summary>
    /// One page of the staff work queue, projected in SQL (no aggregate loading), plus the total and
    /// the overdue count of the whole queue (search narrows the rows, not the overdue count).
    /// </summary>
    Task<WorkQueuePage> GetWorkQueueAsync(WorkQueueFilter filter, CancellationToken ct);

    /// <summary>
    /// Assigned claims in a market that are open or were decided since <paramref name="decidedSince"/>:
    /// the input for the team workload view (narrow projection, bounded by open workload).
    /// </summary>
    Task<IReadOnlyList<WorkloadClaimRow>> GetWorkloadRowsAsync(Market market, DateTimeOffset decidedSince, CancellationToken ct);

    Task<int> CountUnassignedOpenAsync(Market market, CancellationToken ct);

    void Add(Claim claim);

    /// <summary>Next value of the claim number sequence (unique across concurrent submissions).</summary>
    Task<long> NextClaimSequenceAsync(CancellationToken ct);
}

public interface IPolicyRepository
{
    Task<Policy?> GetAsync(string policyNumber, CancellationToken ct);
    Task<IReadOnlyList<Policy>> GetByHolderAsync(Guid holderUserId, CancellationToken ct);
}

public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken ct);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct);

    /// <summary>Claims officers and managers of a market.</summary>
    Task<IReadOnlyList<User>> GetStaffAsync(Market market, CancellationToken ct);
}

/// <summary>Commits all changes of the current request in one transaction (claim + children + outbox rows).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}

public enum WorkQueueOrder
{
    SlaDueAt = 1,   // most urgent first
    ReportedAt = 2, // oldest first
}

/// <summary>
/// AssignedTo null = the unassigned queue of the market; otherwise that officer's claims.
/// Without a status filter, closed claims are left out (a queue is open work).
/// </summary>
public sealed record WorkQueueFilter(
    Market Market, Guid? AssignedTo, ClaimStatus? Status, WorkQueueOrder Order, string? Search, PageRequest Paging,
    DateTimeOffset Now);

/// <param name="Rows">The requested page.</param>
/// <param name="TotalCount">Rows matching the filter and search, across all pages.</param>
/// <param name="OverdueCount">Overdue claims in the whole queue (ignores search).</param>
public sealed record WorkQueuePage(IReadOnlyList<WorkQueueRow> Rows, int TotalCount, int OverdueCount);

public sealed record ClaimantClaimsFilter(Guid ClaimantId, string? Search, PageRequest Paging);

/// <param name="AwaitingInformationCount">Claims waiting on the claimant, over all their claims.</param>
/// <param name="OldestAwaitingInformationId">The one to answer first, for a direct link.</param>
public sealed record ClaimantClaimsPage(
    IReadOnlyList<Claim> Claims, int TotalCount, int AwaitingInformationCount, Guid? OldestAwaitingInformationId);

/// <summary>1-based page number and page size (validated by the API: 1..100).</summary>
public sealed record PageRequest(int Page, int PageSize)
{
    public int Skip => (Page - 1) * PageSize;
}

public sealed record WorkQueueRow(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    Market Market,
    ClaimStatus Status,
    Priority Priority,
    DateTimeOffset ReportedAt,
    DateTimeOffset SlaDueAt,
    DateTimeOffset? DecisionAt,
    Guid? AssignedOfficerId,
    decimal OutstandingReserve,
    string Currency);

public interface IOutboxRepository
{
    /// <summary>Unpublished messages in insertion order (tracked, so they can be marked and saved).</summary>
    Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int take, CancellationToken ct);
}

public interface IExposureRepository
{
    /// <summary>
    /// Applies an exposure delta exactly once per (consumer group, event id): the read-model update
    /// and the ProcessedEvents row commit in one transaction. Returns false if it was already applied.
    /// </summary>
    Task<bool> ApplyOnceAsync(string consumerGroup, Guid eventId, ExposureKey key, ExposureDelta delta, DateTimeOffset eventAt, CancellationToken ct);

    Task<IReadOnlyList<ExposureByMarket>> GetAllAsync(CancellationToken ct);
}

public sealed record ExposureKey(Market Market, string Currency, ClaimType ClaimType);

public sealed record ExposureDelta(int OpenClaims, decimal OutstandingReserve);

public sealed record WorkloadClaimRow(
    Guid AssignedOfficerId, ClaimStatus Status, DateTimeOffset ReportedAt, DateTimeOffset SlaDueAt, DateTimeOffset? DecisionAt);

public interface INotificationRepository
{
    /// <summary>
    /// Stores the notifications for an event exactly once per (consumer group, event id).
    /// Returns false for a duplicate delivery (nothing stored, nothing should be pushed).
    /// </summary>
    Task<bool> AddOnceAsync(string consumerGroup, Guid eventId, IReadOnlyList<Notification> notifications, CancellationToken ct);

    Task<IReadOnlyList<Notification>> GetForUserAsync(Guid userId, int take, CancellationToken ct);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct);

    /// <summary>Tracked, so it can be marked read and saved.</summary>
    Task<Notification?> GetAsync(Guid id, CancellationToken ct);

    Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset at, CancellationToken ct);
}
