using System.Security.Claims;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Presentation.Auth;
using ChubbClaims.Service.DTOs.Auth;
using ChubbClaims.Tests.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace ChubbClaims.Tests.Auth;

/// <summary>Token issued at dev login → validated → read back by CurrentUser (the path every request takes).</summary>
public class DevTokenTests
{
    private static readonly JwtSettings Settings = new()
    {
        Issuer = "test-issuer", Audience = "test-audience", SigningKey = new string('k', 48), LifetimeMinutes = 60,
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static async Task<ClaimsPrincipal> IssueAndValidate(UserResponse user)
    {
        var token = new DevTokenIssuer(Options.Create(Settings), new FixedTimeProvider(Now)).Issue(user).AccessToken;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Settings.Issuer,
            ValidAudience = Settings.Audience,
            IssuerSigningKey = DevTokenIssuer.SigningKey(Settings),
            NameClaimType = AppClaimTypes.Name,
            RoleClaimType = AppClaimTypes.Role,
        });
        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    private static CurrentUser CurrentUserFor(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });

    [Fact]
    public async Task Staff_token_carries_id_role_and_market()
    {
        var officer = new UserResponse(Guid.NewGuid(), "Hafiz", "officer.my@demo.local", UserRole.ClaimsOfficer, Market.MY);

        var principal = await IssueAndValidate(officer);
        var current = CurrentUserFor(principal);

        current.UserId.ShouldBe(officer.Id);
        current.Role.ShouldBe(UserRole.ClaimsOfficer);
        current.Market.ShouldBe(Market.MY);
        principal.IsInRole("ClaimsOfficer").ShouldBeTrue(); // what [Authorize(Policy = Staff)] checks
    }

    [Fact]
    public async Task Claimant_token_has_no_market()
    {
        var claimant = new UserResponse(Guid.NewGuid(), "Aisyah", "claimant.my@demo.local", UserRole.Claimant, null);

        var current = CurrentUserFor(await IssueAndValidate(claimant));

        current.Role.ShouldBe(UserRole.Claimant);
        current.Market.ShouldBeNull();
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var user = new UserResponse(Guid.NewGuid(), "x", "x@demo.local", UserRole.Claimant, null);
        var token = new DevTokenIssuer(Options.Create(Settings), new FixedTimeProvider(Now)).Issue(user).AccessToken;

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Settings.Issuer,
            ValidAudience = Settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(new byte[48]),
        });

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Token_expires_after_the_configured_lifetime()
    {
        var user = new UserResponse(Guid.NewGuid(), "x", "x@demo.local", UserRole.Claimant, null);

        var response = new DevTokenIssuer(Options.Create(Settings), new FixedTimeProvider(Now)).Issue(user);

        response.ExpiresAt.ShouldBe(Now.AddMinutes(60));
    }
}
