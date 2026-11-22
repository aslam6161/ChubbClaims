using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChubbClaims.Presentation.Middleware;

/// <summary>
/// Answers 413 with a plain ProblemDetails when the declared body is too large, before model
/// binding starts reading it. Without this, the multipart reader's limit surfaces as a vague 400
/// ("Multipart body length limit exceeded"). <c>[RequestSizeLimit]</c> stays as the backstop for a
/// body that doesn't declare its length.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RejectLargeRequestsAttribute(long maxBytes, string detail) : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.HttpContext.Request.ContentLength is not { } length || length <= maxBytes) return;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = "File too large",
            Detail = detail,
        };
        if (context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
            problem.Extensions["correlationId"] = correlationId;

        context.Result = new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status413PayloadTooLarge,
            ContentTypes = { "application/problem+json" },
        };
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
