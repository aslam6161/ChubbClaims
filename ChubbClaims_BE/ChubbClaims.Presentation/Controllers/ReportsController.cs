using ChubbClaims.Presentation.Auth;
using ChubbClaims.Service.DTOs.Reports;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

[Route("api/reports")]
[Tags("Manager")]
[Authorize(Policy = AuthorizationPolicies.ManagerOnly)]
public sealed class ReportsController(IExposureService exposure) : BaseApiController
{
    /// <summary>
    /// Outstanding liability by market, currency and claim type, from the event-fed read model.
    /// Eventually consistent: each row carries lastEventAt. Totals are per currency (no FX).
    /// </summary>
    [HttpGet("exposure")]
    [EndpointSummary("Exposure by market")]
    public Task<ExposureReportResponse> Exposure(CancellationToken ct) => exposure.GetAsync(ct);
}
