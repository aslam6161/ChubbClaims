using System.Security.Claims;
using System.Text;
using ChubbClaims.Service.DTOs.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ChubbClaims.Presentation.Auth;

/// <summary>
/// Issues a signed JWT for a seeded user. A clearly labelled development shortcut standing in for
/// a real identity provider: the rest of the API only depends on the token's claims, so swapping
/// in Entra ID / Cognito later changes the token source, not the authorization code.
/// </summary>
public sealed class DevTokenIssuer(IOptions<JwtSettings> options, TimeProvider clock)
{
    private readonly JwtSettings _settings = options.Value;

    public DevLoginResponse Issue(UserResponse user)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_settings.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(AppClaimTypes.UserId, user.Id.ToString()),
            new(AppClaimTypes.Name, user.Name),
            new(AppClaimTypes.Email, user.Email),
            new(AppClaimTypes.Role, user.Role.ToString()),
        };
        if (user.Market is { } market)
            claims.Add(new Claim(AppClaimTypes.Market, market.ToString()));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(_settings), SecurityAlgorithms.HmacSha256),
        });

        return new DevLoginResponse(token, expires, user);
    }

    public static SymmetricSecurityKey SigningKey(JwtSettings settings) => new(Encoding.UTF8.GetBytes(settings.SigningKey));
}
