using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.Team;

namespace ChubbClaims.Service.Helper;

/// <summary>Turns assigned-claim rows into per-officer workload figures (pure, unit tested).</summary>
public static class WorkloadCalculator
{
    public static readonly TimeSpan DecisionsWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan AverageWindow = TimeSpan.FromDays(30);

    public static IReadOnlyList<OfficerWorkloadResponse> Calculate(
        IReadOnlyList<User> staff, IReadOnlyList<WorkloadClaimRow> rows, DateTimeOffset now)
    {
        var byOfficer = rows.ToLookup(r => r.AssignedOfficerId);

        return staff
            .Select(officer =>
            {
                var mine = byOfficer[officer.Id].ToList();
                var open = mine.Where(r => r.Status is not (ClaimStatus.Settled or ClaimStatus.Rejected)).ToList();
                var decided = mine.Where(r => r.DecisionAt is { } d && d >= now - AverageWindow).ToList();

                return new OfficerWorkloadResponse(
                    officer.Id,
                    officer.Name,
                    officer.Role,
                    OpenClaims: open.Count,
                    UnderReview: open.Count(r => r.Status == ClaimStatus.UnderReview),
                    AwaitingClaimant: open.Count(r => ClaimProgress.WaitingOnFor(r.Status) == WaitingOn.Claimant),
                    ApprovedAwaitingPayment: open.Count(r => r.Status == ClaimStatus.Approved),
                    Overdue: open.Count(r => ClaimProgress.IsOverdue(r.SlaDueAt, r.DecisionAt, now)),
                    OldestOpenAgeDays: open.Count == 0 ? null : (int)open.Max(r => now - r.ReportedAt).TotalDays,
                    DecisionsLast7Days: decided.Count(r => r.DecisionAt >= now - DecisionsWindow),
                    AverageDaysToDecision: decided.Count == 0
                        ? null
                        : Math.Round(decided.Average(r => (r.DecisionAt!.Value - r.ReportedAt).TotalDays), 1));
            })
            .OrderByDescending(o => o.Overdue).ThenByDescending(o => o.OpenClaims).ThenBy(o => o.Name)
            .ToList();
    }
}
