using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Settings;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs;
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

    public async Task<MyClaimsPageResponse> GetMineAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var result = await claims.GetByClaimantAsync(
            new ClaimantClaimsFilter(currentUser.UserId, Paging.NormalizeSearch(search), new PageRequest(page, pageSize)), ct);
        return new MyClaimsPageResponse(
            result.Claims.Select(c => c.ToSummary()).ToList(), page, pageSize, result.TotalCount,
            result.AwaitingInformationCount, result.OldestAwaitingInformationId);
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

    // ---------------------------------------------------------------- Officer actions

    public Task PickUpAsync(Guid claimId, CancellationToken ct) =>
        ActAsync(claimId, "picked up", (claim, actor, now) => claim.PickUp(actor, now), ct);

    public Task AdjustReserveAsync(Guid claimId, AdjustReserveRequest request, CancellationToken ct) =>
        ActAsync(claimId, "reserve adjusted",
            (claim, actor, now) => claim.AdjustReserve(actor, request.Amount!.Value, request.Reason, now), ct);

    public Task RequestInformationAsync(Guid claimId, RequestInformationRequest request, CancellationToken ct) =>
        ActAsync(claimId, "information requested",
            (claim, actor, now) => claim.RequestInformation(actor, request.Question, now), ct);

    public Task ApproveAsync(Guid claimId, ApproveClaimRequest request, CancellationToken ct) =>
        ActAsync(claimId, "approved",
            (claim, actor, now) => claim.Approve(actor, request.SettlementAmount!.Value, request.Note, now), ct);

    public Task SettleAsync(Guid claimId, CancellationToken ct) =>
        ActAsync(claimId, "settled", (claim, actor, now) => claim.Settle(actor, now), ct);

    public Task RejectAsync(Guid claimId, RejectClaimRequest request, CancellationToken ct) =>
        ActAsync(claimId, "rejected", (claim, actor, now) => claim.Reject(actor, request.Reason, now), ct);

    public Task AddNoteAsync(Guid claimId, AddNoteRequest request, CancellationToken ct) =>
        ActAsync(claimId, "note added", (claim, actor, now) => claim.AddNote(actor, request.Text, now), ct);

    /// <summary>
    /// The shape of every officer action: load a claim the caller may see → domain method → save.
    /// If another officer saved the same claim in between, SaveChanges fails on RowVersion → 409.
    /// </summary>
    private async Task ActAsync(Guid claimId, string what, Action<Claim, Actor, DateTimeOffset> action, CancellationToken ct)
    {
        var claim = await LoadVisibleAsync(claimId, ct);
        var from = claim.Status;

        action(claim, currentUser.Actor, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Claim {ClaimNumber} {Action} by {ActorRole} {ActorId}: {From} -> {To}",
            claim.ClaimNumber, what, currentUser.Role, currentUser.UserId, from, claim.Status);
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
