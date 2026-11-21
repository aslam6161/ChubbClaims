using ChubbClaims.Service.DTOs.Auth;
using ChubbClaims.Service.DTOs.Claims;
using ChubbClaims.Service.DTOs.Policies;

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
}
