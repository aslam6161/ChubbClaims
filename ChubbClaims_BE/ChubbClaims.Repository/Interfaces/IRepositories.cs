using ChubbClaims.Domain.Entity;

namespace ChubbClaims.Repository.Interfaces;

public interface IClaimRepository
{
    /// <summary>The full aggregate (with children), tracked for changes. Null if it does not exist.</summary>
    Task<Claim?> GetAsync(Guid id, CancellationToken ct);

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
}

/// <summary>Commits all changes of the current request in one transaction (claim + children + outbox rows).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
