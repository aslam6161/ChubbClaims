using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Settings;
using ChubbClaims.Presentation.Middleware;
using ChubbClaims.Repository.Interfaces;
using Microsoft.Extensions.Options;

namespace ChubbClaims.Presentation.Extensions;

public static class PresentationServiceExtensions
{
    public const string FrontendCorsPolicy = "Frontend";

    /// <summary>HTTP-layer services: controllers, ProblemDetails, OpenAPI, CORS, plus host-level services (clock, correlation, settings).</summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers().AddJsonOptions(o => ApiJson.Configure(o.JsonSerializerOptions));
        services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions)); // the OpenAPI generator reads these
        services.AddHttpContextAccessor();
        services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<ClaimRulesSettings>()
            .Bind(configuration.GetSection(ClaimRulesSettings.SectionName))
            .Validate(s => Enum.GetValues<Market>().All(s.Markets.ContainsKey), "ClaimRules:Markets must configure every market")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ClaimRulesSettings>>().Value);

        services.AddProblemDetails(options =>
        {
            // Every error response carries the correlation id so a user-reported error can be found in the logs.
            options.CustomizeProblemDetails = ctx =>
            {
                if (ctx.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
                    ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
            };
        });
        services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Claims Platform API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials() // needed later for the SignalR connection
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

        return services;
    }
}
