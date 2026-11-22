using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ChubbClaims.Repository.Repositories;

public sealed class ClaimRepository(DataContext db) : IClaimRepository
{
    public Task<Claim?> GetAsync(Guid id, CancellationToken ct) =>
        db.Claims
            .Include(c => c.InformationRequests)
            .Include(c => c.StatusHistory)
            .Include(c => c.ReserveHistory)
            .Include(c => c.Notes)
            .AsSplitQuery() // four collections: avoid a cartesian-product join
            .SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<ClaimantClaimsPage> GetByClaimantAsync(ClaimantClaimsFilter filter, CancellationToken ct)
    {
        var mine = db.Claims.AsNoTracking().Where(c => c.ClaimantId == filter.ClaimantId); // IX_Claims_ClaimantId

        // Over all their claims, so "1 claim needs information from you" doesn't depend on search or page.
        var awaiting = mine.Where(c => c.Status == ClaimStatus.AwaitingInformation);
        var awaitingCount = await awaiting.CountAsync(ct);
        var oldestAwaiting = awaitingCount == 0
            ? null
            : await awaiting.OrderBy(c => c.ReportedAt).Select(c => (Guid?)c.Id).FirstAsync(ct);

        if (filter.Search is { } term)
            mine = mine.Where(c => c.ClaimNumber.Contains(term) || c.PolicyNumber.Contains(term)
                || c.IncidentLocation.Contains(term) || c.Description.Contains(term));

        var total = await mine.CountAsync(ct);
        var page = await mine
            .OrderByDescending(c => c.ReportedAt).ThenByDescending(c => c.ClaimNumber) // stable across pages
            .Skip(filter.Paging.Skip).Take(filter.Paging.PageSize)
            .ToListAsync(ct);
        return new ClaimantClaimsPage(page, total, awaitingCount, oldestAwaiting);
    }

    public async Task<WorkQueuePage> GetWorkQueueAsync(WorkQueueFilter filter, CancellationToken ct)
    {
        // Unassigned: IX_Claims_Status_Market. Mine: IX_Claims_AssignedOfficerId_Status.
        var query = db.Claims.AsNoTracking().Where(c => c.Market == filter.Market);

        query = filter.AssignedTo is { } officerId
            ? query.Where(c => c.AssignedOfficerId == officerId)
            : query.Where(c => c.AssignedOfficerId == null);

        query = filter.Status is { } status
            ? query.Where(c => c.Status == status)
            : query.Where(c => c.Status != ClaimStatus.Settled && c.Status != ClaimStatus.Rejected);

        // Same rule as ClaimProgress.IsOverdue, written out so SQL can count it. Whole queue: ignores search.
        var overdue = await query.CountAsync(c => c.DecisionAt == null && c.SlaDueAt < filter.Now, ct);

        if (filter.Search is { } term)
            query = query.Where(c => c.ClaimNumber.Contains(term) || c.PolicyNumber.Contains(term)
                || c.IncidentLocation.Contains(term));

        var total = await query.CountAsync(ct);

        query = filter.Order == WorkQueueOrder.SlaDueAt
            ? query.OrderBy(c => c.SlaDueAt).ThenBy(c => c.ReportedAt).ThenBy(c => c.ClaimNumber)
            : query.OrderBy(c => c.ReportedAt).ThenBy(c => c.ClaimNumber); // tie-breaker: stable across pages

        var rows = await query
            .Skip(filter.Paging.Skip).Take(filter.Paging.PageSize)
            .Select(c => new WorkQueueRow(
                c.Id, c.ClaimNumber, c.Type, c.Market, c.Status, c.Priority, c.ReportedAt, c.SlaDueAt, c.DecisionAt,
                c.AssignedOfficerId,
                // Same rule as Claim.OutstandingReserve, written out so SQL can compute it.
                c.Status == ClaimStatus.Settled || c.Status == ClaimStatus.Rejected ? 0m : c.ReserveAmount - c.PaidAmount,
                c.Currency))
            .ToListAsync(ct);
        return new WorkQueuePage(rows, total, overdue);
    }

    public async Task<IReadOnlyList<WorkloadClaimRow>> GetWorkloadRowsAsync(Market market, DateTimeOffset decidedSince, CancellationToken ct) =>
        await db.Claims.AsNoTracking()
            .Where(c => c.Market == market && c.AssignedOfficerId != null)
            .Where(c => (c.Status != ClaimStatus.Settled && c.Status != ClaimStatus.Rejected) || c.DecisionAt >= decidedSince)
            .Select(c => new WorkloadClaimRow(c.AssignedOfficerId!.Value, c.Status, c.ReportedAt, c.SlaDueAt, c.DecisionAt))
            .ToListAsync(ct);

    public Task<int> CountUnassignedOpenAsync(Market market, CancellationToken ct) =>
        db.Claims.CountAsync(c => c.Market == market && c.AssignedOfficerId == null
                                  && c.Status != ClaimStatus.Settled && c.Status != ClaimStatus.Rejected, ct);

    public void Add(Claim claim) => db.Claims.Add(claim);

    public async Task<long> NextClaimSequenceAsync(CancellationToken ct)
    {
        // ToListAsync, not SingleAsync: a LINQ operator makes EF wrap the SQL in a subquery,
        // and SQL Server does not allow NEXT VALUE FOR inside one (error 11719).
        var values = await db.Database
            .SqlQueryRaw<long>("SELECT NEXT VALUE FOR dbo." + DataContext.ClaimNumberSequence + " AS [Value]")
            .ToListAsync(ct);
        return values.Single();
    }
}

public sealed class PolicyRepository(DataContext db) : IPolicyRepository
{
    public Task<Policy?> GetAsync(string policyNumber, CancellationToken ct) =>
        db.Policies.AsNoTracking().SingleOrDefaultAsync(p => p.PolicyNumber == policyNumber, ct);

    public async Task<IReadOnlyList<Policy>> GetByHolderAsync(Guid holderUserId, CancellationToken ct) =>
        await db.Policies.AsNoTracking()
            .Where(p => p.HolderUserId == holderUserId)
            .OrderBy(p => p.PolicyNumber)
            .ToListAsync(ct);
}

public sealed class UserRepository(DataContext db) : IUserRepository
{
    public Task<User?> GetAsync(Guid id, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<User>> GetStaffAsync(Market market, CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .Where(u => u.Market == market && (u.Role == UserRole.ClaimsOfficer || u.Role == UserRole.ClaimsManager))
            .OrderBy(u => u.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Market).ThenBy(u => u.Email).ToListAsync(ct);
}

public sealed class UnitOfWork(DataContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
