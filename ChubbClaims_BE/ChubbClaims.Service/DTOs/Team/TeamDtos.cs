using ChubbClaims.Domain.Enums;

namespace ChubbClaims.Service.DTOs.Team;

/// <summary>Workload and recent performance of the claims staff in one market.</summary>
public sealed record TeamWorkloadResponse(
    Market Market,
    DateTimeOffset GeneratedAt,
    int UnassignedOpenClaims,
    IReadOnlyList<OfficerWorkloadResponse> Officers);

/// <summary>
/// Open counts are by status (assigned claims are never "Submitted"). Decisions = approve/reject.
/// AverageDaysToDecision covers decisions in the last 30 days; null when there were none.
/// </summary>
public sealed record OfficerWorkloadResponse(
    Guid OfficerId,
    string Name,
    UserRole Role,
    int OpenClaims,
    int UnderReview,
    int AwaitingClaimant,
    int ApprovedAwaitingPayment,
    int Overdue,
    int? OldestOpenAgeDays,
    int DecisionsLast7Days,
    double? AverageDaysToDecision);
