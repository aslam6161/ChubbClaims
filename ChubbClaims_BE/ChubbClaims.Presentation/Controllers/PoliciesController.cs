using ChubbClaims.Presentation.Auth;
using ChubbClaims.Service.DTOs.Policies;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

[Route("api/policies")]
[Tags("Claimant")]
[Authorize(Policy = AuthorizationPolicies.ClaimantOnly)]
public sealed class PoliciesController(IPolicyService policies) : BaseApiController
{
    /// <summary>The caller's policies, to choose from when reporting an incident.</summary>
    [HttpGet("mine")]
    [EndpointSummary("My policies")]
    public Task<IReadOnlyList<PolicyResponse>> Mine(CancellationToken ct) => policies.GetMineAsync(ct);
}
