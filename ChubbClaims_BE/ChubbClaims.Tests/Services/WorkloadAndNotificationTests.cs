using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.ReadModels;
using ChubbClaims.Presentation.Hubs;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Helper;
using ChubbClaims.Service.Services;
using NSubstitute;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Services;

public class WorkloadCalculatorTests
{
    private static readonly User Hafiz = new("Hafiz", "officer.my@demo.local", UserRole.ClaimsOfficer, Market.MY);
    private static readonly User Farah = new("Farah", "manager.my@demo.local", UserRole.ClaimsManager, Market.MY);

    private static WorkloadClaimRow Row(User officer, ClaimStatus status, int reportedDaysAgo, int slaInDays = 5, int? decidedDaysAgo = null) =>
        new(officer.Id, status, Now.AddDays(-reportedDaysAgo), Now.AddDays(slaInDays), decidedDaysAgo is { } d ? Now.AddDays(-d) : null);

    [Fact]
    public void Counts_open_work_overdue_and_recent_decisions_per_officer()
    {
        var rows = new[]
        {
            Row(Hafiz, ClaimStatus.UnderReview, reportedDaysAgo: 12, slaInDays: -2),                       // overdue
            Row(Hafiz, ClaimStatus.AwaitingInformation, reportedDaysAgo: 3),
            Row(Hafiz, ClaimStatus.Approved, reportedDaysAgo: 9, slaInDays: -1, decidedDaysAgo: 2),        // decided in time: not overdue
            Row(Hafiz, ClaimStatus.Settled, reportedDaysAgo: 25, slaInDays: -15, decidedDaysAgo: 20),      // decided 20 days ago
        };

        var hafiz = WorkloadCalculator.Calculate([Hafiz, Farah], rows, Now).Single(o => o.OfficerId == Hafiz.Id);

        (hafiz.OpenClaims, hafiz.UnderReview, hafiz.AwaitingClaimant, hafiz.ApprovedAwaitingPayment).ShouldBe((3, 1, 1, 1));
        hafiz.Overdue.ShouldBe(1);
        hafiz.OldestOpenAgeDays.ShouldBe(12);
        hafiz.DecisionsLast7Days.ShouldBe(1);
        hafiz.AverageDaysToDecision.ShouldBe(6.0); // (7 + 5) / 2 over the last 30 days
    }

    [Fact]
    public void Staff_without_claims_are_listed_with_zeros_and_no_averages()
    {
        var farah = WorkloadCalculator.Calculate([Hafiz, Farah], [], Now).Single(o => o.OfficerId == Farah.Id);

        (farah.OpenClaims, farah.Overdue, farah.DecisionsLast7Days).ShouldBe((0, 0, 0));
        farah.OldestOpenAgeDays.ShouldBeNull();
        farah.AverageDaysToDecision.ShouldBeNull();
    }

    [Fact]
    public void Officers_with_overdue_work_come_first()
    {
        var result = WorkloadCalculator.Calculate([Hafiz, Farah], [Row(Farah, ClaimStatus.UnderReview, 12, slaInDays: -1)], Now);

        result[0].OfficerId.ShouldBe(Farah.Id);
    }
}

public class NotificationServiceTests
{
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private NotificationService ServiceFor(ChubbClaims.Domain.ValueObjects.Actor caller) =>
        new(_notifications, _unitOfWork, new TestCurrentUser(caller), new FixedTimeProvider(Now));

    [Fact]
    public async Task Owner_can_mark_read()
    {
        var notification = new Notification(Claimant.UserId, Guid.NewGuid(), "CLM-MY-2026-000001", "Hi", Now);
        _notifications.GetAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);

        await ServiceFor(Claimant).MarkReadAsync(notification.Id, CancellationToken.None);

        notification.ReadAt.ShouldBe(Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_elses_notification_looks_missing()
    {
        var notification = new Notification(Claimant.UserId, Guid.NewGuid(), "CLM-MY-2026-000001", "Hi", Now);
        _notifications.GetAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);

        await Should.ThrowAsync<NotFoundException>(() => ServiceFor(OtherClaimant).MarkReadAsync(notification.Id, CancellationToken.None));

        notification.IsRead.ShouldBeFalse();
    }
}

public class ClaimsHubGroupsTests
{
    [Fact]
    public void Groups_follow_role_and_market()
    {
        ClaimsHubGroups.For(Claimant.UserId, UserRole.Claimant, null).ShouldBe([$"user:{Claimant.UserId}"]);
        ClaimsHubGroups.For(Officer.UserId, UserRole.ClaimsOfficer, Market.MY).ShouldBe([$"user:{Officer.UserId}", "staff:MY"]);
        ClaimsHubGroups.For(Manager.UserId, UserRole.ClaimsManager, Market.MY).ShouldBe([$"user:{Manager.UserId}", "staff:MY", "managers"]);
    }
}
