using ChubbClaims.Presentation.Auth;
using ChubbClaims.Service.DTOs.Auth;
using ChubbClaims.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChubbClaims.Presentation.Controllers;

[Route("api")]
[Tags("Auth")]
public sealed class AuthController(IAccountService accounts, DevTokenIssuer tokenIssuer, IWebHostEnvironment environment)
    : BaseApiController
{
    /// <summary>DEVELOPMENT ONLY: sign in as a seeded user by email (no password). Stands in for a real identity provider.</summary>
    [AllowAnonymous]
    [HttpPost("auth/dev-login")]
    [EndpointSummary("Dev login (Development only)")]
    [ProducesResponseType<DevLoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<DevLoginResponse>> DevLogin(DevLoginRequest request, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return NotFound();

        var user = await accounts.GetByEmailAsync(request.Email, ct);
        return tokenIssuer.Issue(user);
    }

    /// <summary>DEVELOPMENT ONLY: the seeded users, for the login picker.</summary>
    [AllowAnonymous]
    [HttpGet("auth/dev-users")]
    [EndpointSummary("Seeded users (Development only)")]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> DevUsers(CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            return NotFound();

        return Ok(await accounts.ListDevUsersAsync(ct));
    }

    /// <summary>The signed-in user.</summary>
    [HttpGet("me")]
    [EndpointSummary("Current user")]
    public Task<UserResponse> Me(CancellationToken ct) => accounts.GetCurrentAsync(ct);
}
