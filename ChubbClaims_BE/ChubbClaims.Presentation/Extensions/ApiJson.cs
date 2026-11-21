using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChubbClaims.Presentation.Extensions;

/// <summary>
/// The one JSON format of the API, applied to controllers, the OpenAPI generator and SignalR, so
/// what the docs say, what the API writes and what the hub pushes cannot drift apart.
/// </summary>
public static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        // Enums as strings ("Motor", not 1): readable and stable if enum order changes.
        options.Converters.Add(new JsonStringEnumConverter());

        // Numbers must be JSON numbers. The web default also accepts "123" in quotes, and the OpenAPI
        // generator then documents every number as `number | string`, which leaks into the generated
        // frontend types. A quoted number is now a 400.
        options.NumberHandling = JsonNumberHandling.Strict;
    }
}
