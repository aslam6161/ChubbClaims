using ChubbClaims.Repository.Interfaces;

namespace ChubbClaims.Presentation.Middleware;

/// <summary>Exposes the id set by <see cref="CorrelationIdMiddleware"/>; null outside an HTTP request.</summary>
public sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string? CorrelationId =>
        accessor.HttpContext?.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var id) == true ? id as string : null;
}
