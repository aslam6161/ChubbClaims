using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Tests.Services;

/// <summary>
/// Hand-written instead of NSubstitute: ICurrentUser has default interface members (Actor, IsStaff)
/// that a proxy would override with defaults.
/// </summary>
internal sealed class TestCurrentUser(Actor actor) : ICurrentUser
{
    public Guid UserId => actor.UserId;
    public UserRole Role => actor.Role;
    public Market? Market => actor.Market;
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
