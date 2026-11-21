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
}
