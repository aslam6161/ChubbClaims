using System.ComponentModel.DataAnnotations;
using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Service.DTOs.Claims;

// ---------------------------------------------------------------- Requests
// DataAnnotations catch shape errors (400) at the edge; business rules stay in the domain (422).
// Enums are non-nullable with [EnumDataType] (a missing value binds to 0, which is not defined -> 400):
// a nullable enum here would make the shared OpenAPI enum schema nullable for every response too.

public sealed record SubmitClaimRequest(
    [Required, MaxLength(32)] string PolicyNumber,
    [EnumDataType(typeof(ClaimType))] ClaimType Type,
    [Required] DateOnly? IncidentDate,
    [Required, MaxLength(500)] string IncidentLocation,
    [Required, MaxLength(4000)] string Description,
    [Range(typeof(decimal), "0.01", "9999999999999999")] decimal? EstimatedLossAmount,
    MotorDetailsDto? Motor,
    PropertyDetailsDto? Property);

public sealed record MotorDetailsDto(
    [Required, MaxLength(20)] string VehicleRegistration,
    bool ThirdPartyInvolved,
    [MaxLength(50)] string? PoliceReportNumber);

public sealed record PropertyDetailsDto(
    [Required, MaxLength(500)] string PropertyAddress,
    [EnumDataType(typeof(DamageType))] DamageType DamageType);

public sealed record InformationResponseRequest([Required, MaxLength(4000)] string Response);

// ---------------------------------------------------------------- Responses

public sealed record SubmitClaimResponse(Guid Id, string ClaimNumber, ClaimStatus Status);

/// <summary>
/// One page of the claimant's claims. AwaitingYouCount and OldestAwaitingYouClaimId cover all their
/// claims (not just the search or page), for the "needs information from you" banner and its link.
/// </summary>
public sealed record MyClaimsPageResponse(
    IReadOnlyList<ClaimSummaryResponse> Items, int Page, int PageSize, int TotalCount,
    int AwaitingYouCount, Guid? OldestAwaitingYouClaimId);

public sealed record ClaimSummaryResponse(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    ClaimStatus Status,
    Market Market,
    string Currency,
    string PolicyNumber,
    DateOnly IncidentDate,
    DateTimeOffset ReportedAt,
    decimal? SettlementAmount,
    DateTimeOffset? DecisionAt);

/// <summary>
/// One claim. Fields under "staff only" are null for claimants: the reserve is an internal
/// liability estimate, and notes are internal.
/// </summary>
public sealed record ClaimDetailResponse(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    ClaimStatus Status,
    Market Market,
    string Currency,
    string PolicyNumber,
    DateOnly IncidentDate,
    DateTimeOffset ReportedAt,
    string IncidentLocation,
    string Description,
    decimal? EstimatedLossAmount,
    MotorDetailsDto? Motor,
    PropertyDetailsDto? Property,
    decimal? SettlementAmount,
    decimal PaidAmount,
    DateTimeOffset? DecisionAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset SlaDueAt,
    InformationRequestResponse? OpenInformationRequest,
    IReadOnlyList<InformationRequestResponse> InformationRequests,
    IReadOnlyList<StatusChangeResponse> StatusHistory,
    IReadOnlyList<ClaimDocumentResponse> Documents,
    // staff only
    Guid? AssignedOfficerId,
    Priority? Priority,
    decimal? ReserveAmount,
    decimal? OutstandingReserve,
    IReadOnlyList<ReserveChangeResponse>? ReserveHistory,
    IReadOnlyList<NoteResponse>? Notes);

public sealed record InformationRequestResponse(
    Guid Id, string Question, DateTimeOffset RequestedAt, string? Response, DateTimeOffset? RespondedAt, bool IsOpen);

public sealed record StatusChangeResponse(ClaimStatus? From, ClaimStatus To, UserRole ActorRole, DateTimeOffset At, string? Reason);

public sealed record ReserveChangeResponse(decimal OldAmount, decimal NewAmount, Guid ActorId, DateTimeOffset At, string Reason);

/// <summary>A photo or document on the claim. The file itself is read from its own endpoint.</summary>
public sealed record ClaimDocumentResponse(
    Guid Id, string FileName, string ContentType, long SizeBytes, UserRole UploadedByRole, DateTimeOffset UploadedAt);

public sealed record NoteResponse(Guid Id, Guid AuthorId, string Text, DateTimeOffset CreatedAt);
