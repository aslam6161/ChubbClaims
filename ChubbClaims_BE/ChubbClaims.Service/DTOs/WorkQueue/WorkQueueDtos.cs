using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;

namespace ChubbClaims.Service.DTOs.WorkQueue;

public enum WorkQueueView
{
    Unassigned = 1, // incoming claims in my market nobody has picked up
    Mine = 2,       // my open claims
}

public enum WorkQueueSort
{
    Sla = 1,      // most urgent first
    Reported = 2, // oldest first
}

/// <summary>
/// One page of the work queue. TotalCount counts rows matching the search across all pages;
/// OverdueCount is for the whole queue (ignores search), so the header doesn't change as you search.
/// </summary>
public sealed record WorkQueuePageResponse(
    IReadOnlyList<WorkQueueItemResponse> Items, int Page, int PageSize, int TotalCount, int OverdueCount);

/// <summary>One row of the work queue: what an officer needs to decide what to work on next.</summary>
public sealed record WorkQueueItemResponse(
    Guid Id,
    string ClaimNumber,
    ClaimType Type,
    Market Market,
    ClaimStatus Status,
    Priority Priority,
    DateTimeOffset ReportedAt,
    int AgeDays,
    DateTimeOffset SlaDueAt,
    bool IsOverdue,
    WaitingOn WaitingOn,
    Guid? AssignedOfficerId,
    decimal OutstandingReserve,
    string Currency);
