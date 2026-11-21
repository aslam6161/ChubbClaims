using ChubbClaims.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace ChubbClaims.Presentation.Auth;

/// <summary>
/// Role-based policies at the endpoint (coarse: who may call it at all). Resource-based checks
/// (owns this claim, same market, assigned officer) happen in the service and the aggregate.
/// </summary>
public static class AuthorizationPolicies
{
    public const string ClaimantOnly = nameof(ClaimantOnly);
    public const string Staff = nameof(Staff);
    public const string ManagerOnly = nameof(ManagerOnly);

    public static AuthorizationBuilder AddClaimsPolicies(this AuthorizationBuilder builder) => builder
        // Every endpoint requires a signed-in user unless it says [AllowAnonymous].
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
        .AddPolicy(ClaimantOnly, p => p.RequireRole(nameof(UserRole.Claimant)))
        .AddPolicy(Staff, p => p.RequireRole(nameof(UserRole.ClaimsOfficer), nameof(UserRole.ClaimsManager)))
        .AddPolicy(ManagerOnly, p => p.RequireRole(nameof(UserRole.ClaimsManager)));
}
