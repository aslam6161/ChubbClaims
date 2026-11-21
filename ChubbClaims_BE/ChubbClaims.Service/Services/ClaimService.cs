using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Settings;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Helper;
using ChubbClaims.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Service.Services;

/// <summary>
/// Claim use cases. Each one orchestrates: load → check visibility → call the domain method →
/// save. Business rules (and who-may-act checks) live in the Claim aggregate, not here.
/// </summary>
public sealed class ClaimService(
    IClaimRepository claims,
    IPolicyRepository policies,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ClaimRulesSettings rules,
    TimeProvider clock,
    ILogger<ClaimService> logger) : IClaimService
{
    public async Task<SubmitClaimResponse> SubmitAsync(SubmitClaimRequest request, CancellationToken ct)
    {
        // Another person's policy looks exactly like a missing one: no way to probe policy numbers.
        var policy = await policies.GetAsync(request.PolicyNumber.Trim(), ct);
        if (policy is null || policy.HolderUserId != currentUser.UserId)
            throw new NotFoundException($"Policy {request.PolicyNumber} was not found.");

        var now = clock.GetUtcNow();
        // Sequence values are not reused if validation then fails; gaps in claim numbers are acceptable.
        var number = ClaimNumber.Create(policy.Market, now.Year, await claims.NextClaimSequenceAsync(ct));

        var claim = Claim.Submit(
            request.ToIncidentReport(), policy, currentUser.Actor, number, rules.For(policy.Market, request.Type), now);

        claims.Add(claim);
        await unitOfWork.SaveChangesAsync(ct); // claim + ClaimSubmitted outbox row in one transaction

        logger.LogInformation("Claim {ClaimNumber} submitted in {Market} ({ClaimType}), reserve {Reserve} {Currency}",
            claim.ClaimNumber, claim.Market, claim.Type, claim.ReserveAmount, claim.Currency);
        return new SubmitClaimResponse(claim.Id, claim.ClaimNumber, claim.Status);
    }

    public async Task<IReadOnlyList<ClaimSummaryResponse>> GetMineAsync(CancellationToken ct)
    {
        var mine = await claims.GetByClaimantAsync(currentUser.UserId, ct);
        return mine.Select(c => c.ToSummary()).ToList();
    }

    public async Task<ClaimDetailResponse> GetAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await LoadVisibleAsync(claimId, ct);
        return claim.ToDetail(includeStaffFields: currentUser.IsStaff);
    }

    public async Task RespondToInformationRequestAsync(
        Guid claimId, Guid requestId, InformationResponseRequest request, CancellationToken ct)
    {
        var claim = await LoadVisibleAsync(claimId, ct);

        claim.ProvideInformation(currentUser.Actor, requestId, request.Response, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Claim {ClaimNumber}: claimant answered information request {RequestId}", claim.ClaimNumber, requestId);
    }

    /// <summary>
    /// Resource-level visibility: the owning claimant, or staff in the claim's market.
    /// Everyone else gets 404, the same as for a claim that does not exist.
    /// </summary>
    private async Task<Claim> LoadVisibleAsync(Guid claimId, CancellationToken ct)
    {
        var claim = await claims.GetAsync(claimId, ct);
        var visible = claim is not null && (currentUser.Role == UserRole.Claimant
            ? claim.ClaimantId == currentUser.UserId
            : currentUser.IsStaff && claim.Market == currentUser.Market);

        return visible ? claim! : throw new NotFoundException($"Claim {claimId} was not found.");
    }
}
