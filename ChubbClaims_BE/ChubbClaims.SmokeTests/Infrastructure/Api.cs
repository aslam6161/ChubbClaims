using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ChubbClaims.SmokeTests.Infrastructure;

/// <summary>
/// The running API, called the way any client would: HTTP and JSON, nothing shared with the server's code.
/// Point it elsewhere with the SMOKE_API_URL environment variable (default http://localhost:5080).
/// </summary>
public static class Api
{
    public static readonly Uri BaseUrl = new(Environment.GetEnvironmentVariable("SMOKE_API_URL") ?? "http://localhost:5080");

    private static readonly HttpClient Http = new() { BaseAddress = BaseUrl, Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, Task<string>> Tokens = new();
    private static readonly Lazy<bool> Running = new(Probe);

    /// <summary>Used by <see cref="SmokeFactAttribute"/>: any answer from /health counts, even "unhealthy",
    /// so a broken stack fails the tests instead of skipping them.</summary>
    public static bool IsRunning => Running.Value;

    public static string NotRunningMessage =>
        $"The API isn't running at {BaseUrl} (start it with `docker compose up`, or set SMOKE_API_URL)";

    private static bool Probe()
    {
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            probe.GetAsync(new Uri(BaseUrl, "/health")).GetAwaiter().GetResult().Dispose();
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>A dev-login token for a seeded user, e.g. "claimant.my" (fetched once per run).</summary>
    public static Task<string> Token(string user) => Tokens.GetOrAdd(user, async u =>
    {
        var login = await Post("/api/auth/dev-login", null, new { email = $"{u}@demo.local" });
        login.Status.ShouldBe(HttpStatusCode.OK, $"dev-login for {u}: {login}");
        return login.Body!["accessToken"]!.GetValue<string>();
    });

    public static Task<ApiResponse> Get(string path, string? asUser = null) => Send(HttpMethod.Get, path, asUser);

    public static Task<ApiResponse> Post(string path, string? asUser, object? body = null) =>
        Send(HttpMethod.Post, path, asUser, body);

    public static Task<ApiResponse> Put(string path, string? asUser, object? body = null) =>
        Send(HttpMethod.Put, path, asUser, body);

    public static async Task<ApiResponse> Send(HttpMethod method, string path, string? asUser, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        return await Send(request, asUser);
    }

    /// <summary>A multipart upload with one file, as a browser's file input sends it.</summary>
    public static async Task<ApiResponse> Upload(Guid claimId, string asUser, string fileName, byte[] content, string field = "file")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/claims/{claimId}/documents")
        {
            Content = new MultipartFormDataContent { { file, field, fileName } },
        };
        return await Send(request, asUser);
    }

    private static async Task<ApiResponse> Send(HttpRequestMessage request, string? asUser)
    {
        if (asUser is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token(asUser));
        var response = await Http.SendAsync(request, TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json") == true;
        return new ApiResponse(response, bytes, isJson && bytes.Length > 0 ? JsonNode.Parse(bytes) : null);
    }

    /// <summary>
    /// Sends only the request line and headers, declaring a body that never comes, and returns the
    /// answer. Shows the API refuses an upload from its declared size, before reading the body.
    /// </summary>
    public static async Task<(int Status, JsonNode? Body)> SendHeadersOnly(string method, string path, string asUser,
        IReadOnlyDictionary<string, string> headers)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(BaseUrl.Host, BaseUrl.Port, TestContext.Current.CancellationToken);
        await using var stream = tcp.GetStream();
        var head = new StringBuilder($"{method} {path} HTTP/1.1\r\nHost: {BaseUrl.Authority}\r\n")
            .Append($"Authorization: Bearer {await Token(asUser)}\r\nConnection: close\r\n");
        foreach (var (name, value) in headers) head.Append($"{name}: {value}\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.Append("\r\n").ToString()), TestContext.Current.CancellationToken);

        // Read one response, not until the connection closes: the server may keep it open to drain
        // the body it refused. A server that waits for the body instead never answers, and times out.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var text = "";
        var buffer = new byte[4096];
        while (!IsComplete(text))
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0) break;
            text += Encoding.UTF8.GetString(buffer, 0, read);
        }

        var status = int.Parse(text.Split(' ', 3)[1]);
        var body = text[(text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
        var start = body.IndexOf('{'); // chunk sizes may surround it; the JSON object is all we need
        return (status, start < 0 ? null : JsonNode.Parse(body[start..(body.LastIndexOf('}') + 1)]));

        static bool IsComplete(string response)
        {
            var end = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) return false;
            var head = response[..end].ToLowerInvariant();
            var body = response[(end + 4)..];
            if (head.Contains("transfer-encoding: chunked")) return body.EndsWith("0\r\n\r\n", StringComparison.Ordinal);
            var length = head.Split("\r\n").FirstOrDefault(l => l.StartsWith("content-length:"));
            return length is null || Encoding.UTF8.GetByteCount(body) >= int.Parse(length["content-length:".Length..].Trim());
        }
    }

    /// <summary>Polls until <paramref name="ready"/> returns a value, or gives up and returns null.</summary>
    public static async Task<T?> WaitFor<T>(Func<Task<T?>> ready, TimeSpan? timeout = null) where T : class
    {
        var end = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            if (await ready() is { } value) return value;
            if (DateTime.UtcNow > end) return null;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Read models fed by Kafka are eventually consistent: events from earlier tests may still be
    /// arriving. Returns the value once two reads <paramref name="gap"/> apart agree.
    /// </summary>
    public static async Task<T> Settled<T>(Func<Task<T>> read, Func<T, T, bool> same, TimeSpan? gap = null)
    {
        var previous = await read();
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(gap ?? TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
            var current = await read();
            if (same(previous, current)) return current;
            previous = current;
        }
        throw new TimeoutException("The read model kept changing for 30 seconds.");
    }

    public static void Log(string message) => TestContext.Current.TestOutputHelper?.WriteLine(message);
}

/// <summary>One answer from the API: status, headers, the raw bytes, and the JSON body if there is one.</summary>
public sealed record ApiResponse(HttpResponseMessage Response, byte[] Bytes, JsonNode? Body)
{
    public HttpStatusCode Status => Response.StatusCode;

    /// <summary>The ProblemDetails "detail", if any.</summary>
    public string? Detail => Body is JsonObject o && o["detail"] is JsonValue v ? v.GetValue<string>() : null;

    public JsonNode Json => Body ?? throw new ShouldAssertException($"Expected a JSON body: {this}");

    public JsonArray Items => Json["items"]!.AsArray();

    public override string ToString()
    {
        var text = Body?.ToJsonString() ?? $"({Bytes.Length} bytes)";
        return $"{(int)Status} {(text.Length > 300 ? text[..300] + "…" : text)}";
    }
}
