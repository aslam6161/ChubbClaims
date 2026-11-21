using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.ValueObjects;

namespace ChubbClaims.Service.Interfaces;

/// <summary>The authenticated caller, read from the JWT by the host. Services never see HTTP types.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    UserRole Role { get; }

    /// <summary>Set for staff; null for claimants.</summary>
    Market? Market { get; }

    Actor Actor => new(UserId, Role, Market);
    bool IsStaff => Actor.IsStaff;
}
