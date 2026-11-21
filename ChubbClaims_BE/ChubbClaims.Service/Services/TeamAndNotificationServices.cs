using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Notifications;
using ChubbClaims.Service.DTOs.Team;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Helper;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Service.Services;

/// <summary>
/// Team workload for the caller's market, computed from a narrow query over assigned claims that are
/// open or recently decided (bounded by open workload). At larger scale this would become a
/// projection like exposure.
/// </summary>
public sealed class TeamService(IClaimRepository claims, IUserRepository users, ICurrentUser currentUser, TimeProvider clock)
    : ITeamService
{
    public async Task<TeamWorkloadResponse> GetWorkloadAsync(CancellationToken ct)
    {
        var market = currentUser.Market ?? throw new ForbiddenException("Only claims staff have a team view.");
        var now = clock.GetUtcNow();

        var staff = await users.GetStaffAsync(market, ct);
        var rows = await claims.GetWorkloadRowsAsync(market, now - WorkloadCalculator.AverageWindow, ct);
        var unassigned = await claims.CountUnassignedOpenAsync(market, ct);

        return new TeamWorkloadResponse(market, now, unassigned, WorkloadCalculator.Calculate(staff, rows, now));
    }
}

public sealed class NotificationService(
    INotificationRepository notifications, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock) : INotificationService
{
    public const int MaxItems = 50;

    public async Task<NotificationListResponse> GetMineAsync(CancellationToken ct)
    {
        var items = await notifications.GetForUserAsync(currentUser.UserId, MaxItems, ct);
        var unread = await notifications.CountUnreadAsync(currentUser.UserId, ct);

        return new NotificationListResponse(unread,
            items.Select(n => new NotificationResponse(n.Id, n.ClaimId, n.ClaimNumber, n.Message, n.CreatedAt, n.IsRead)).ToList());
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken ct)
    {
        var notification = await notifications.GetAsync(notificationId, ct);
        if (notification is null || notification.UserId != currentUser.UserId)
            throw new NotFoundException($"Notification {notificationId} was not found."); // someone else's looks missing

        notification.MarkRead(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
    }

    public Task MarkAllReadAsync(CancellationToken ct) =>
        notifications.MarkAllReadAsync(currentUser.UserId, clock.GetUtcNow(), ct);
}
