using ChubbClaims.Domain.Enums;
using ChubbClaims.Presentation.Auth;
using System.ComponentModel.DataAnnotations;
using ChubbClaims.Service.DTOs;
using ChubbClaims.Service.DTOs.WorkQueue;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

[Route("api/work-queue")]
[Tags("Staff")]
[Authorize(Policy = AuthorizationPolicies.Staff)]
public sealed class WorkQueueController(IWorkQueueService workQueue) : BaseApiController
{
    /// <summary>
    /// One page of the caller's market queue. `view=unassigned`: claims nobody has picked up;
    /// `view=mine`: my open claims. Optional `status` filter (also shows closed claims when asked).
    /// `search` matches claim number, policy number or incident location. Sorted by SLA by default.
    /// </summary>
    [HttpGet]
    [EndpointSummary("Work queue")]
    public Task<WorkQueuePageResponse> Get(
        [FromQuery] WorkQueueView view = WorkQueueView.Unassigned,
        [FromQuery] ClaimStatus? status = null,
        [FromQuery] WorkQueueSort sort = WorkQueueSort.Sla,
        [FromQuery, StringLength(Paging.MaxSearchLength)] string? search = null,
        [FromQuery, Range(1, Paging.MaxPage)] int page = 1,
        [FromQuery, Range(1, Paging.MaxPageSize)] int pageSize = Paging.DefaultPageSize,
        CancellationToken ct = default) =>
        workQueue.GetAsync(view, status, sort, search, page, pageSize, ct);
}
