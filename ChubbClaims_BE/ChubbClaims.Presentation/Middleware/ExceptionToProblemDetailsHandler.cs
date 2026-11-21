using ChubbClaims.Domain.Exceptions;
using ChubbClaims.Service.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Middleware;

/// <summary>
/// Maps known exceptions to RFC 9457 ProblemDetails responses with the right status code.
/// Unknown exceptions become a 500 without internal details.
/// EF Core concurrency (→ 409) is added in the persistence phase.
/// </summary>
public sealed class ExceptionToProblemDetailsHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ExceptionToProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception");
        else
            logger.LogInformation("Request rejected with {StatusCode}: {Reason}", status, exception.Message);

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // Expected business errors are safe to explain; unexpected ones must not leak internals.
                Detail = status >= StatusCodes.Status500InternalServerError ? null : exception.Message,
            },
        });
    }

    internal static (int Status, string Title) Map(Exception exception) => exception switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
        ForbiddenException or ClaimAccessDeniedException => (StatusCodes.Status403Forbidden, "Action not allowed"),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict with current state"),
        InvalidClaimTransitionException => (StatusCodes.Status409Conflict, "Action not allowed in the claim's current status"),
        DomainValidationException => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
    };
}
