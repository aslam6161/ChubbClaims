using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace ChubbClaims.SmokeTests.Infrastructure;

/// <summary>A push from the claims hub, with the time it arrived.</summary>
public sealed record HubMessage(string Type, JsonElement Data, DateTime At)
{
    public string? Get(string field) =>
        Data.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public bool IsNull(string field) => Data.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Null;

    public bool IsAbout(Guid claimId, string? status = null) =>
        Type == "ClaimUpdated" && Get("claimId") == claimId.ToString() && (status is null || Get("status") == status);
}

/// <summary>One user's connection to /hubs/claims (the same hub the web app uses), recording every push.</summary>
public sealed class HubListener : IAsyncDisposable
{
    private readonly ConcurrentQueue<HubMessage> received = new();

    private HubListener(string user, HubConnection connection)
    {
        User = user;
        Connection = connection;
        foreach (var type in new[] { "ClaimUpdated", "ExposureUpdated" })
            connection.On<JsonElement>(type, data => received.Enqueue(new HubMessage(type, data, DateTime.UtcNow)));
    }

    public string User { get; }
    public HubConnection Connection { get; }
    public IEnumerable<HubMessage> Received => received;

    public static HubConnection Build(Func<Task<string?>>? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Api.BaseUrl, "/hubs/claims"), o => { if (token is not null) o.AccessTokenProvider = token; })
            .Build();

    public static async Task<HubListener> Connect(string user)
    {
        var token = await Api.Token(user);
        var listener = new HubListener(user, Build(() => Task.FromResult<string?>(token)));
        await listener.Connection.StartAsync(TestContext.Current.CancellationToken);
        return listener;
    }

    /// <summary>The first push matching <paramref name="match"/>, waiting for it if need be; null if none arrives.</summary>
    public Task<HubMessage?> WaitFor(Func<HubMessage, bool> match, TimeSpan? timeout = null) =>
        Api.WaitFor(() => Task.FromResult(received.FirstOrDefault(match)), timeout ?? TimeSpan.FromSeconds(8));

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
