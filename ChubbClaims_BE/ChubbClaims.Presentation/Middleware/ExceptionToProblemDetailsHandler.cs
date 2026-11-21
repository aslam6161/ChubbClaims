using ChubbClaims.Service.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Middleware;

/// <summary>
/// Maps known exceptions to RFC 9457 ProblemDetails responses with the right status code.
/// Unknown exceptions become a 500 without internal details.
/// Domain exceptions (invalid transition → 409, rule violation → 422) and EF concurrency (→ 409)
/// are added to this mapping in the phases that introduce them.
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
        ForbiddenException => (StatusCodes.Status403Forbidden, "Action not allowed"),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict with current state"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
    };
}
