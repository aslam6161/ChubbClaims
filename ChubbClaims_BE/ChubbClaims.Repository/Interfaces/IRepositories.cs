using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Repository.Interfaces;

public interface IClaimRepository
{
    /// <summary>The full aggregate (with children), tracked for changes. Null if it does not exist.</summary>
    Task<Claim?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>The claimant's claims, newest first, without children (read-only list).</summary>
    Task<IReadOnlyList<Claim>> GetByClaimantAsync(Guid claimantId, CancellationToken ct);

    /// <summary>One page of the staff work queue, projected in SQL (no aggregate loading).</summary>
    Task<IReadOnlyList<WorkQueueRow>> GetWorkQueueAsync(WorkQueueFilter filter, CancellationToken ct);

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
public sealed record WorkQueueFilter(Market Market, Guid? AssignedTo, ClaimStatus? Status, WorkQueueOrder Order, int Take);

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
