using System.Reflection;
using System.Text.Json;
using ChubbClaims.Presentation.Controllers;
using ChubbClaims.Presentation.Extensions;
using ChubbClaims.Service.DTOs.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ChubbClaims.Tests.Auth;

/// <summary>
/// Guards the OpenAPI contract the Angular app generates its types from. Both rules below were
/// real bugs found by generating the frontend client.
/// </summary>
public class ApiContractTests
{
    private static IEnumerable<MethodInfo> Actions() =>
        typeof(BaseApiController).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));

    /// <summary>Unwraps Task&lt;T&gt; / ActionResult&lt;T&gt;; null when the action has no typed body (IActionResult, Task).</summary>
    private static Type? BodyType(Type returnType)
    {
        var type = returnType;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            type = type.GetGenericArguments()[0];
        else if (type == typeof(Task))
            return null;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>))
            return type.GetGenericArguments()[0];

        return typeof(IActionResult).IsAssignableFrom(type) ? null : type;
    }

    [Fact]
    public void Actions_that_declare_response_types_also_declare_their_success_type()
    {
        // Once an action has any [ProducesResponseType], ASP.NET stops inferring the 200 from its
        // return type, so the OpenAPI document (and the generated TS client) loses the body type.
        var missing = Actions()
            .Where(a => BodyType(a.ReturnType) is not null)
            .Where(a =>
            {
                var declared = a.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
                return declared.Count > 0 && !declared.Any(r => r.StatusCode is >= 200 and < 300);
            })
            .Select(a => $"{a.DeclaringType!.Name}.{a.Name}")
            .ToList();

        missing.ShouldBeEmpty();
    }

    private static ServiceProvider ConfiguredServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddPresentation(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

    [Fact]
    public void Controllers_and_the_OpenAPI_generator_use_the_same_strict_json_format()
    {
        using var services = ConfiguredServices();

        var mvc = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        var http = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        foreach (var options in new[] { mvc, http })
        {
            options.NumberHandling.ShouldBe(System.Text.Json.Serialization.JsonNumberHandling.Strict);
            options.Converters.ShouldContain(c => c is System.Text.Json.Serialization.JsonStringEnumConverter);
        }
    }

    [Fact]
    public void A_quoted_number_is_rejected_and_a_real_number_accepted()
    {
        using var services = ConfiguredServices();
        var options = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<AdjustReserveRequest>("""{"amount":"5000","reason":"x"}""", options));
        JsonSerializer.Deserialize<AdjustReserveRequest>("""{"amount":5000,"reason":"x"}""", options)!.Amount.ShouldBe(5000m);
    }
}
