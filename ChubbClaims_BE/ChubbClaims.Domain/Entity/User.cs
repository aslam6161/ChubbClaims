using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Domain.ValueObjects;

namespace ChubbClaims.Domain.Entity;

/// <summary>Seeded dev user (a real system would get identities from an identity provider).</summary>
public sealed class User : BaseEntity
{
    private User() { } // EF Core

    public User(string name, string email, UserRole role, Market? market)
    {
        if (role != UserRole.Claimant && market is null)
            throw new DomainValidationException("Claims staff must belong to a market.");

        Name = name;
        Email = email;
        Role = role;
        Market = market;
    }

    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public UserRole Role { get; private set; }

    /// <summary>Set for staff (their work is scoped to one market); null for claimants.</summary>
    public Market? Market { get; private set; }

    public Actor AsActor() => new(Id, Role, Market);
}
