using ChubbClaims.Domain.Enums;
using ChubbClaims.Messaging.Realtime;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ChubbClaims.Presentation.Hubs;

/// <summary>Messages the server sends; the browser listens for these names.</summary>
public interface IClaimsClient
{
    Task ClaimUpdated(ClaimUpdatedMessage message);
    Task ExposureUpdated(ExposureUpdatedMessage message);
}

/// <summary>
/// Server-to-client only (no hub methods). On connect, the caller joins the groups their role
/// entitles them to; the server decides membership from the JWT, never from client input.
/// </summary>
[Authorize]
public sealed class ClaimsHub(ICurrentUser currentUser, ILogger<ClaimsHub> logger) : Hub<IClaimsClient>
{
    public const string Path = "/hubs/claims";

    public override async Task OnConnectedAsync()
    {
        var groups = ClaimsHubGroups.For(currentUser.UserId, currentUser.Role, currentUser.Market);
        foreach (var group in groups)
            await Groups.AddToGroupAsync(Context.ConnectionId, group);

        logger.LogDebug("Hub connection {ConnectionId} for {Role} {UserId} joined {Groups}",
            Context.ConnectionId, currentUser.Role, currentUser.UserId, groups);
        await base.OnConnectedAsync();
    }
}

public static class ClaimsHubGroups
{
    public static string User(Guid userId) => $"user:{userId}";
    public static string MarketStaff(Market market) => $"staff:{market}";
    public const string Managers = "managers";

    public static IReadOnlyList<string> For(Guid userId, UserRole role, Market? market)
    {
        var groups = new List<string> { User(userId) };
        if (role is UserRole.ClaimsOfficer or UserRole.ClaimsManager && market is { } m)
            groups.Add(MarketStaff(m));
        if (role == UserRole.ClaimsManager)
            groups.Add(Managers);
        return groups;
    }
}

/// <summary>
/// SignalR implementation of the consumers' push port. Single instance: with several API instances a
/// backplane (Redis or Azure SignalR) is needed, because the consumer that handles an event only
/// reaches the browsers connected to its own instance.
/// </summary>
public sealed class SignalRRealtimePush(IHubContext<ClaimsHub, IClaimsClient> hub) : IRealtimePush
{
    public Task ClaimUpdatedAsync(ClaimUpdatedMessage message, Guid? claimantId, CancellationToken ct)
    {
        var groups = new List<string> { ClaimsHubGroups.MarketStaff(message.Market) };
        if (claimantId is { } id)
            groups.Add(ClaimsHubGroups.User(id));
        return hub.Clients.Groups(groups).ClaimUpdated(message);
    }

    public Task ExposureUpdatedAsync(ExposureUpdatedMessage message, CancellationToken ct) =>
        hub.Clients.Group(ClaimsHubGroups.Managers).ExposureUpdated(message);
}
