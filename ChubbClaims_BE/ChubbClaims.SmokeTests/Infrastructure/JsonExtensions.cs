namespace ChubbClaims.SmokeTests.Infrastructure;

/// <summary>Short reads of the API's JSON (camelCase, enums as strings).</summary>
public static class JsonExtensions
{
    public static string Str(this JsonNode node, string key) => node[key]!.GetValue<string>();
    public static decimal Num(this JsonNode node, string key) => node[key]!.GetValue<decimal>();
    public static int Int(this JsonNode node, string key) => node[key]!.GetValue<int>();
    public static bool Bool(this JsonNode node, string key) => node[key]!.GetValue<bool>();
    public static Guid Id(this JsonNode node, string key = "id") => node[key]!.GetValue<Guid>();
    public static bool IsNull(this JsonNode node, string key) => node[key] is null;
    public static JsonNode[] List(this JsonNode node, string key = "items") => [.. node[key]!.AsArray().Select(n => n!)];
}
