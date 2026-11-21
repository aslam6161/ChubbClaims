using ChubbClaims.Domain.Enums;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Presentation.Auth;

/// <summary>Reads the caller from the validated JWT. Only used on endpoints that require authentication.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new InvalidOperationException("No authenticated user on this request.");

    public Guid UserId => Guid.Parse(Required(AppClaimTypes.UserId));

    public UserRole Role => Enum.Parse<UserRole>(Required(AppClaimTypes.Role));

    public Market? Market => Principal.FindFirst(AppClaimTypes.Market)?.Value is { } value ? Enum.Parse<Market>(value) : null;

    private string Required(string claimType) =>
        Principal.FindFirst(claimType)?.Value ?? throw new InvalidOperationException($"Token has no '{claimType}' claim.");
}
