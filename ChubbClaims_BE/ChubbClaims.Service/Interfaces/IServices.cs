using ChubbClaims.Domain.Enums;
using ChubbClaims.Service.DTOs.Auth;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.DTOs.Policies;
using ChubbClaims.Service.DTOs.WorkQueue;

namespace ChubbClaims.Service.Interfaces;

public interface IAccountService
{
    /// <summary>Dev login: the seeded user with this email, or NotFound.</summary>
    Task<UserResponse> GetByEmailAsync(string email, CancellationToken ct);

    Task<UserResponse> GetCurrentAsync(CancellationToken ct);

    /// <summary>Seeded users for the dev login picker (Development only).</summary>
    Task<IReadOnlyList<UserResponse>> ListDevUsersAsync(CancellationToken ct);
}

public interface IPolicyService
{
    Task<IReadOnlyList<PolicyResponse>> GetMineAsync(CancellationToken ct);
}

public interface IClaimService
{
    /// <summary>First notice of loss by the current claimant.</summary>
    Task<SubmitClaimResponse> SubmitAsync(SubmitClaimRequest request, CancellationToken ct);

    Task<IReadOnlyList<ClaimSummaryResponse>> GetMineAsync(CancellationToken ct);

    /// <summary>
    /// The claim if the caller may see it: the claimant who owns it, or staff in its market.
    /// Anyone else gets NotFound (not Forbidden), so claim ids cannot be probed.
    /// </summary>
    Task<ClaimDetailResponse> GetAsync(Guid claimId, CancellationToken ct);

    Task RespondToInformationRequestAsync(Guid claimId, Guid requestId, InformationResponseRequest request, CancellationToken ct);

    // Officer actions. Each loads a claim the caller may see (staff in its market), calls the
    // aggregate method (which enforces lifecycle + assigned-officer rules) and saves.
    Task PickUpAsync(Guid claimId, CancellationToken ct);
    Task AdjustReserveAsync(Guid claimId, AdjustReserveRequest request, CancellationToken ct);
    Task RequestInformationAsync(Guid claimId, RequestInformationRequest request, CancellationToken ct);
    Task ApproveAsync(Guid claimId, ApproveClaimRequest request, CancellationToken ct);
    Task SettleAsync(Guid claimId, CancellationToken ct);
    Task RejectAsync(Guid claimId, RejectClaimRequest request, CancellationToken ct);
    Task AddNoteAsync(Guid claimId, AddNoteRequest request, CancellationToken ct);
}

public interface IWorkQueueService
{
    /// <summary>The caller's market queue: unassigned claims, or the caller's own open claims.</summary>
    Task<IReadOnlyList<WorkQueueItemResponse>> GetAsync(WorkQueueView view, ClaimStatus? status, WorkQueueSort sort, CancellationToken ct);
}
