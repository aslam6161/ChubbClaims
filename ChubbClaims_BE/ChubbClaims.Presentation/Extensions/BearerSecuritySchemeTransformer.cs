using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ChubbClaims.Presentation.Extensions;

/// <summary>
/// Adds a JWT Bearer security scheme to the OpenAPI document so Scalar shows an "Authorize"
/// option and sends <c>Authorization: Bearer &lt;token&gt;</c> on requests.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeId = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Dev token from POST /api/auth/dev-login",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
        });

        return Task.CompletedTask;
    }
}
