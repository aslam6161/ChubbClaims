using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Rules;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.DTOs.WorkQueue;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Services;
using NSubstitute;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Services;

public class WorkQueueServiceTests
{
    private readonly IClaimRepository _claims = Substitute.For<IClaimRepository>();

    private WorkQueueService ServiceFor(Actor caller) =>
        new(_claims, new TestCurrentUser(caller), new FixedTimeProvider(Now));

    private static WorkQueueRow Row(ClaimStatus status, DateTimeOffset reportedAt, DateTimeOffset slaDueAt, DateTimeOffset? decisionAt = null) =>
        new(Guid.NewGuid(), "CLM-MY-2026-000001", ClaimType.Motor, Market.MY, status, Priority.Normal,
            reportedAt, slaDueAt, decisionAt, null, 5_000m, "MYR");

    [Fact]
    public async Task Unassigned_view_queries_the_callers_market_without_an_assignee()
    {
        _claims.GetWorkQueueAsync(default!, default).ReturnsForAnyArgs([]);

        await ServiceFor(Officer).GetAsync(WorkQueueView.Unassigned, null, WorkQueueSort.Sla, CancellationToken.None);

        await _claims.Received(1).GetWorkQueueAsync(
            new WorkQueueFilter(Market.MY, null, null, WorkQueueOrder.SlaDueAt, WorkQueueService.MaxRows), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mine_view_queries_the_callers_own_claims_with_filters()
    {
        _claims.GetWorkQueueAsync(default!, default).ReturnsForAnyArgs([]);

        await ServiceFor(SgOfficer).GetAsync(WorkQueueView.Mine, ClaimStatus.Approved, WorkQueueSort.Reported, CancellationToken.None);

        await _claims.Received(1).GetWorkQueueAsync(
            new WorkQueueFilter(Market.SG, SgOfficer.UserId, ClaimStatus.Approved, WorkQueueOrder.ReportedAt, WorkQueueService.MaxRows),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rows_get_age_overdue_and_waiting_on()
    {
        _claims.GetWorkQueueAsync(default!, default).ReturnsForAnyArgs(
        [
            Row(ClaimStatus.Submitted, Now.AddDays(-12), slaDueAt: Now.AddDays(-2)),                   // overdue
            Row(ClaimStatus.AwaitingInformation, Now.AddDays(-3), slaDueAt: Now.AddDays(7)),           // on time
            Row(ClaimStatus.Approved, Now.AddDays(-15), slaDueAt: Now.AddDays(-5), decisionAt: Now.AddDays(-6)), // decided in time
        ]);

        var rows = await ServiceFor(Officer).GetAsync(WorkQueueView.Mine, null, WorkQueueSort.Sla, CancellationToken.None);

        rows.Select(r => (r.AgeDays, r.IsOverdue, r.WaitingOn)).ShouldBe(
        [
            (12, true, WaitingOn.Officer),
            (3, false, WaitingOn.Claimant),
            (15, false, WaitingOn.Officer),
        ]);
    }

    [Fact]
    public async Task Claimants_have_no_work_queue()
    {
        await Should.ThrowAsync<ForbiddenException>(() =>
            ServiceFor(Claimant).GetAsync(WorkQueueView.Unassigned, null, WorkQueueSort.Sla, CancellationToken.None));
    }
}
