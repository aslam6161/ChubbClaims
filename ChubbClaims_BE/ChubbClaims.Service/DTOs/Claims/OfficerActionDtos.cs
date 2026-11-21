using System.ComponentModel.DataAnnotations;

namespace ChubbClaims.Service.DTOs.Claims;

// Amounts are nullable + [Required] so a missing amount is a 400, not a silent 0.
// Their business limits (reserve >= 0, settlement > 0) are domain rules and return 422.

public sealed record AdjustReserveRequest(
    [Required] decimal? Amount,
    [Required, MaxLength(2000)] string Reason);

public sealed record RequestInformationRequest([Required, MaxLength(2000)] string Question);

public sealed record ApproveClaimRequest(
    [Required] decimal? SettlementAmount,
    [MaxLength(4000)] string? Note);

public sealed record RejectClaimRequest([Required, MaxLength(2000)] string Reason);

public sealed record AddNoteRequest([Required, MaxLength(4000)] string Text);
