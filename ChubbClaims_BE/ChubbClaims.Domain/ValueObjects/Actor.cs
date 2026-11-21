using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Domain.ValueObjects;

/// <summary>
/// Who is performing an action. Built by the application layer from the authenticated user and
/// passed into every aggregate method, so the aggregate itself enforces ownership and assignment.
/// </summary>
public sealed record Actor(Guid UserId, UserRole Role, Market? Market)
{
    public bool IsStaff => Role is UserRole.ClaimsOfficer or UserRole.ClaimsManager;
}
