using System.Text.RegularExpressions;
using Serilog.Context;

namespace ChubbClaims.Presentation.Middleware;

/// <summary>
/// Reads <c>X-Correlation-Id</c> from the request (or generates one), echoes it on the response,
/// stores it in <see cref="HttpContext.Items"/> and pushes it into the Serilog log context so every
/// log line written while handling the request carries it. Later phases copy it into outbox rows
/// and Kafka headers.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadValidHeader(context) ?? Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }

    // The header is client input that ends up in logs and message headers, so only accept a short,
    // safe token. Anything else is replaced rather than trusted (prevents log injection).
    private static string? ReadValidHeader(HttpContext context)
    {
        var value = context.Request.Headers[HeaderName].ToString();
        return SafeCorrelationId().IsMatch(value) ? value : null;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex SafeCorrelationId();
}
