using System.Text.Json;
using ChubbClaims.Presentation.Middleware;
using ChubbClaims.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ChubbClaims.Tests.Presentation;

public class ExceptionToProblemDetailsHandlerTests
{
    private static async Task<(int Status, JsonElement Body)> Handle(Exception exception)
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        var handler = new ExceptionToProblemDetailsHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            NullLogger<ExceptionToProblemDetailsHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        handled.ShouldBeTrue();

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, body.RootElement);
    }

    [Theory]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(ConflictException), 409)]
    public async Task Maps_expected_exceptions_to_status_and_keeps_the_message(Type exceptionType, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Claim CLM-MY-2026-000001 was changed")!;

        var (status, body) = await Handle(exception);

        status.ShouldBe(expectedStatus);
        body.GetProperty("status").GetInt32().ShouldBe(expectedStatus);
        body.GetProperty("detail").GetString().ShouldBe("Claim CLM-MY-2026-000001 was changed");
    }

    [Fact]
    public async Task Unknown_exceptions_become_500_without_leaking_the_message()
    {
        var (status, body) = await Handle(new InvalidOperationException("connection string Password=secret"));

        status.ShouldBe(500);
        body.TryGetProperty("detail", out _).ShouldBeFalse();
        body.GetRawText().ShouldNotContain("secret");
    }
}
