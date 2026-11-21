using ChubbClaims.Presentation.Auth;
using ChubbClaims.Service.DTOs.Notifications;
using ChubbClaims.Service.DTOs.Team;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

[Route("api/team")]
[Tags("Staff")]
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class TeamController(ITeamService team) : BaseApiController
{
    /// <summary>
    /// Per officer in the caller's market: open claims by status, waiting on the claimant, overdue,
    /// oldest open age, decisions in the last 7 days, average days to decision (last 30 days).
    /// </summary>
    [HttpGet("workload")]
    [EndpointSummary("Team workload")]
    public Task<TeamWorkloadResponse> Workload(CancellationToken ct) => team.GetWorkloadAsync(ct);
}

/// <summary>The caller's in-app notifications (any signed-in user).</summary>
[Route("api/notifications")]
[Tags("Notifications")]
public sealed class NotificationsController(INotificationService notifications) : BaseApiController
{
    [HttpGet]
    [EndpointSummary("My notifications")]
    public Task<NotificationListResponse> Mine(CancellationToken ct) => notifications.GetMineAsync(ct);

    [HttpPost("{id:guid}/read")]
    [EndpointSummary("Mark notification read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [EndpointSummary("Mark all notifications read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
