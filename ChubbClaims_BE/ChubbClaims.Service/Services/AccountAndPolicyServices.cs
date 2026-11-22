using ChubbClaims.Domain.Settings;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Auth;
using ChubbClaims.Service.DTOs.Policies;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Helper;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Service.Services;

public sealed class AccountService(IUserRepository users, ICurrentUser currentUser) : IAccountService
{
    public async Task<UserResponse> GetByEmailAsync(string email, CancellationToken ct) =>
        (await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), ct))?.ToResponse()
        ?? throw new NotFoundException($"No user with email {email}.");

    public async Task<UserResponse> GetCurrentAsync(CancellationToken ct) =>
        (await users.GetAsync(currentUser.UserId, ct))?.ToResponse()
        ?? throw new NotFoundException("The signed-in user no longer exists.");

    public async Task<IReadOnlyList<UserResponse>> ListDevUsersAsync(CancellationToken ct) =>
        (await users.ListAsync(ct)).Select(u => u.ToResponse()).ToList();
}

public sealed class PolicyService(
    IPolicyRepository policies, ICurrentUser currentUser, ClaimRulesSettings rules, TimeProvider clock) : IPolicyService
{
    public async Task<IReadOnlyList<PolicyResponse>> GetMineAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return (await policies.GetByHolderAsync(currentUser.UserId, ct))
            .Select(p => p.ToResponse(now, rules.MaxIncidentAgeDays))
            .ToList();
    }
}
