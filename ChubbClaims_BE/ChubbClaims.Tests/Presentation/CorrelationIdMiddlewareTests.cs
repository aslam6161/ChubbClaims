using ChubbClaims.Presentation.Middleware;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace ChubbClaims.Tests.Presentation;

public class CorrelationIdMiddlewareTests
{
    private static async Task<(HttpContext Context, object? IdSeenByNext)> Run(string? incomingHeader)
    {
        var context = new DefaultHttpContext();
        if (incomingHeader is not null)
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incomingHeader;

        object? seen = null;
        var middleware = new CorrelationIdMiddleware(ctx =>
        {
            seen = ctx.Items[CorrelationIdMiddleware.ItemKey];
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, seen);
    }

    [Fact]
    public async Task Uses_a_valid_incoming_id_and_echoes_it_on_the_response()
    {
        var (context, seen) = await Run("abc-123_X.y");

        seen.ShouldBe("abc-123_X.y");
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().ShouldBe("abc-123_X.y");
    }

    [Fact]
    public async Task Generates_an_id_when_none_is_sent()
    {
        var (context, seen) = await Run(null);

        var id = seen.ShouldBeOfType<string>();
        id.Length.ShouldBe(32); // Guid "N" format
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().ShouldBe(id);
    }

    [Theory]
    [InlineData("evil\r\nInjected: log line")]
    [InlineData("has spaces")]
    [InlineData("")]
    public async Task Replaces_an_unsafe_incoming_id(string unsafeId)
    {
        var (_, seen) = await Run(unsafeId);

        seen.ShouldNotBe(unsafeId);
        seen.ShouldBeOfType<string>().Length.ShouldBe(32);
    }

    [Fact]
    public async Task Replaces_an_incoming_id_longer_than_64_characters()
    {
        var tooLong = new string('a', 65);

        var (_, seen) = await Run(tooLong);

        seen.ShouldNotBe(tooLong);
    }
}
