using ChubbClaims.Domain.Entity;
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

    public async Task<IReadOnlyList<Claim>> GetByClaimantAsync(Guid claimantId, CancellationToken ct) =>
        await db.Claims.AsNoTracking()
            .Where(c => c.ClaimantId == claimantId) // uses IX_Claims_ClaimantId
            .OrderByDescending(c => c.ReportedAt)
            .ToListAsync(ct);

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

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Market).ThenBy(u => u.Email).ToListAsync(ct);
}

public sealed class UnitOfWork(DataContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
