using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Interfaces;

namespace ChubbClaims.Service.Helper;

internal static class ClaimVisibility
{
    /// <summary>
    /// Resource-level visibility: the owning claimant, or staff in the claim's market.
    /// Everyone else gets 404, the same as for a claim that does not exist.
    /// </summary>
    public static async Task<Claim> GetVisibleAsync(
        this IClaimRepository claims, ICurrentUser currentUser, Guid claimId, CancellationToken ct)
    {
        var claim = await claims.GetAsync(claimId, ct);
        var visible = claim is not null && (currentUser.Role == UserRole.Claimant
            ? claim.ClaimantId == currentUser.UserId
            : currentUser.IsStaff && claim.Market == currentUser.Market);

        return visible ? claim! : throw new NotFoundException($"Claim {claimId} was not found.");
    }
}
