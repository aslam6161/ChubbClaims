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

    private void Returns(params WorkQueueRow[] rows) =>
        _claims.GetWorkQueueAsync(default!, default).ReturnsForAnyArgs(new WorkQueuePage(rows, rows.Length, 0));

    [Fact]
    public async Task Unassigned_view_queries_the_callers_market_without_an_assignee()
    {
        Returns();

        await ServiceFor(Officer).GetAsync(WorkQueueView.Unassigned, null, WorkQueueSort.Sla, null, 1, 20, CancellationToken.None);

        await _claims.Received(1).GetWorkQueueAsync(
            new WorkQueueFilter(Market.MY, null, null, WorkQueueOrder.SlaDueAt, null, new PageRequest(1, 20), Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mine_view_queries_the_callers_own_claims_with_filters_search_and_page()
    {
        Returns();

        await ServiceFor(SgOfficer).GetAsync(
            WorkQueueView.Mine, ClaimStatus.Approved, WorkQueueSort.Reported, "  000123  ", 3, 50, CancellationToken.None);

        await _claims.Received(1).GetWorkQueueAsync(
            new WorkQueueFilter(Market.SG, SgOfficer.UserId, ClaimStatus.Approved, WorkQueueOrder.ReportedAt, "000123",
                new PageRequest(3, 50), Now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blank_search_means_no_search()
    {
        Returns();

        await ServiceFor(Officer).GetAsync(WorkQueueView.Unassigned, null, WorkQueueSort.Sla, "   ", 1, 20, CancellationToken.None);

        await _claims.Received(1).GetWorkQueueAsync(
            Arg.Is<WorkQueueFilter>(f => f.Search == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Page_carries_the_total_and_the_overdue_count_from_the_query()
    {
        _claims.GetWorkQueueAsync(default!, default).ReturnsForAnyArgs(
            new WorkQueuePage([Row(ClaimStatus.Submitted, Now.AddDays(-1), Now.AddDays(9))], TotalCount: 41, OverdueCount: 3));

        var page = await ServiceFor(Officer).GetAsync(WorkQueueView.Unassigned, null, WorkQueueSort.Sla, null, 2, 20, CancellationToken.None);

        (page.Page, page.PageSize, page.TotalCount, page.OverdueCount, page.Items.Count).ShouldBe((2, 20, 41, 3, 1));
    }

    [Fact]
    public async Task Rows_get_age_overdue_and_waiting_on()
    {
        Returns(
            Row(ClaimStatus.Submitted, Now.AddDays(-12), slaDueAt: Now.AddDays(-2)),                   // overdue
            Row(ClaimStatus.AwaitingInformation, Now.AddDays(-3), slaDueAt: Now.AddDays(7)),           // on time
            Row(ClaimStatus.Approved, Now.AddDays(-15), slaDueAt: Now.AddDays(-5), decisionAt: Now.AddDays(-6))); // decided in time

        var page = await ServiceFor(Officer).GetAsync(WorkQueueView.Mine, null, WorkQueueSort.Sla, null, 1, 20, CancellationToken.None);

        page.Items.Select(r => (r.AgeDays, r.IsOverdue, r.WaitingOn)).ShouldBe(
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
            ServiceFor(Claimant).GetAsync(WorkQueueView.Unassigned, null, WorkQueueSort.Sla, null, 1, 20, CancellationToken.None));
    }
}
