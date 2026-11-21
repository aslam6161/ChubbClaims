using ChubbClaims.Presentation.Auth;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

/// <summary>
/// Claims. Task-based endpoints (one per action) rather than a generic PATCH of the status:
/// each action has its own rules, authorization and event. Controllers stay thin:
/// bind → call service → return; errors become ProblemDetails in the exception handler.
/// </summary>
[Route("api/claims")]
public sealed class ClaimsController(IClaimService claims) : BaseApiController
{
    /// <summary>Report an incident (first notice of loss). Market and currency come from the policy.</summary>
    [HttpPost]
    [Tags("Claimant")]
    [Authorize(Policy = AuthorizationPolicies.ClaimantOnly)]
    [EndpointSummary("Submit FNOL")]
    [ProducesResponseType<SubmitClaimResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<SubmitClaimResponse>> Submit(SubmitClaimRequest request, CancellationToken ct)
    {
        var created = await claims.SubmitAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>The caller's claims, newest first.</summary>
    [HttpGet("mine")]
    [Tags("Claimant")]
    [Authorize(Policy = AuthorizationPolicies.ClaimantOnly)]
    [EndpointSummary("My claims")]
    public Task<IReadOnlyList<ClaimSummaryResponse>> Mine(CancellationToken ct) => claims.GetMineAsync(ct);

    /// <summary>
    /// One claim, for its claimant or for staff in its market (staff also get reserve, notes and history).
    /// Anyone else gets 404, never 403, so claim ids cannot be probed.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Tags("Claimant", "Staff")]
    [EndpointSummary("Claim detail")]
    [ProducesResponseType<ClaimDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<ClaimDetailResponse> GetById(Guid id, CancellationToken ct) => claims.GetAsync(id, ct);

    /// <summary>Answer the open information request on the caller's claim.</summary>
    [HttpPost("{id:guid}/information-requests/{requestId:guid}/response")]
    [Tags("Claimant")]
    [Authorize(Policy = AuthorizationPolicies.ClaimantOnly)]
    [EndpointSummary("Answer information request")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> RespondToInformationRequest(
        Guid id, Guid requestId, InformationResponseRequest request, CancellationToken ct)
    {
        await claims.RespondToInformationRequestAsync(id, requestId, request, ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- Officer actions
    // Staff policy here; the aggregate additionally requires the caller to be in the claim's
    // market and (except pick-up and notes) to be the assigned officer.

    /// <summary>Take an unassigned claim. 409 if another officer picked it up first.</summary>
    [HttpPost("{id:guid}/pick-up")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Pick up claim")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> PickUp(Guid id, CancellationToken ct)
    {
        await claims.PickUpAsync(id, ct);
        return NoContent();
    }

    /// <summary>Change the reserve (expected liability). Reason required; never negative.</summary>
    [HttpPut("{id:guid}/reserve")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Adjust reserve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> AdjustReserve(Guid id, AdjustReserveRequest request, CancellationToken ct)
    {
        await claims.AdjustReserveAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Ask the claimant a question; the claim waits for their answer.</summary>
    [HttpPost("{id:guid}/information-requests")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Request information")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> RequestInformation(Guid id, RequestInformationRequest request, CancellationToken ct)
    {
        await claims.RequestInformationAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Approve with a settlement amount; the reserve becomes that amount.</summary>
    [HttpPost("{id:guid}/approve")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Approve claim")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Approve(Guid id, ApproveClaimRequest request, CancellationToken ct)
    {
        await claims.ApproveAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Record payment of the approved settlement and close the claim.</summary>
    [HttpPost("{id:guid}/settle")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Settle claim")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Settle(Guid id, CancellationToken ct)
    {
        await claims.SettleAsync(id, ct);
        return NoContent();
    }

    /// <summary>Reject with a reason and close the claim.</summary>
    [HttpPost("{id:guid}/reject")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Reject claim")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Reject(Guid id, RejectClaimRequest request, CancellationToken ct)
    {
        await claims.RejectAsync(id, request, ct);
        return NoContent();
    }

    /// <summary>Add an internal note (not visible to the claimant). Any staff in the claim's market.</summary>
    [HttpPost("{id:guid}/notes")]
    [Tags("Staff")]
    [Authorize(Policy = AuthorizationPolicies.Staff)]
    [EndpointSummary("Add note")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddNote(Guid id, AddNoteRequest request, CancellationToken ct)
    {
        await claims.AddNoteAsync(id, request, ct);
        return NoContent();
    }
}
