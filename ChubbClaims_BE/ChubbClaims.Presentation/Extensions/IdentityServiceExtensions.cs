using ChubbClaims.Presentation.Auth;
using ChubbClaims.Presentation.Hubs;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ChubbClaims.Presentation.Extensions;

public static class IdentityServiceExtensions
{
    /// <summary>JWT bearer authentication, the claims policies, and the current-user accessor.</summary>
    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        var jwt = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
                  ?? throw new InvalidOperationException("Jwt settings are missing.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false; // keep "sub", "role", "market" as issued
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = DevTokenIssuer.SigningKey(jwt),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = AppClaimTypes.Name,
                    RoleClaimType = AppClaimTypes.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                // Browsers cannot set an Authorization header on a WebSocket, so the SignalR client
                // sends the token as ?access_token=. Accepted only on the hub path. (Request logging
                // records the path, not the query string, so the token is not logged.)
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(ClaimsHub.Path))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder().AddClaimsPolicies();

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<DevTokenIssuer>();
        return services;
    }
}
