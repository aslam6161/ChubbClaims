using ChubbClaims.Domain.Constants;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Exceptions;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Domain;

/// <summary>Which incident dates a new claim can use, so the UI only offers what the server accepts.</summary>
public class PolicyReportableDatesTests
{
    private static readonly DateOnly Today = Markets.LocalDate(Market.MY, Now); // 2026-11-21

    [Fact]
    public void Policy_in_force_allows_its_start_up_to_today()
    {
        Policy().ReportableIncidentDates(Today, Rules.MaxIncidentAgeDays)
            .ShouldBe((new DateOnly(2026, 1, 1), Today));
    }

    [Fact]
    public void Expired_policy_still_allows_incidents_during_its_cover()
    {
        var ended = Policy(from: new DateOnly(2025, 6, 1), to: new DateOnly(2026, 5, 31));

        ended.ReportableIncidentDates(Today, Rules.MaxIncidentAgeDays)
            .ShouldBe((Today.AddDays(-Rules.MaxIncidentAgeDays), new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void Cover_that_ended_longer_ago_than_the_reporting_window_allows_nothing()
    {
        var tooLate = Policy(from: new DateOnly(2024, 11, 22), to: Today.AddDays(-Rules.MaxIncidentAgeDays - 1));

        tooLate.ReportableIncidentDates(Today, Rules.MaxIncidentAgeDays).ShouldBeNull();
    }

    [Fact]
    public void Cancelled_policy_allows_nothing()
    {
        Policy(active: false).ReportableIncidentDates(Today, Rules.MaxIncidentAgeDays).ShouldBeNull();
    }

    public static TheoryData<string, DateOnly, DateOnly> Policies => new()
    {
        { "in force", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31) },
        { "ended recently", new DateOnly(2025, 6, 1), new DateOnly(2026, 5, 31) },
        { "ended exactly at the window", new DateOnly(2024, 11, 21), Today.AddDays(-Rules.MaxIncidentAgeDays) },
        { "ended just before the window", new DateOnly(2024, 11, 21), Today.AddDays(-Rules.MaxIncidentAgeDays - 1) },
    };

    /// <summary>The range must agree with Claim.Submit date by date, so the two can't drift apart.</summary>
    [Theory]
    [MemberData(nameof(Policies))]
    public void Range_matches_what_submission_accepts(string _, DateOnly from, DateOnly to)
    {
        var policy = Policy(from: from, to: to);
        var range = policy.ReportableIncidentDates(Today, Rules.MaxIncidentAgeDays);

        var edges = new[] { from, to, Today, Today.AddDays(-Rules.MaxIncidentAgeDays) };
        foreach (var date in edges.SelectMany(d => Enumerable.Range(-2, 5).Select(d.AddDays)))
        {
            var inRange = range is { } r && date >= r.From && date <= r.To;
            var accepted = Accepts(policy, date);
            accepted.ShouldBe(inRange, $"{date:yyyy-MM-dd}: submit accepted={accepted}, range={range}");
        }
    }

    private static bool Accepts(Policy policy, DateOnly date)
    {
        try
        {
            Submit(MotorReport(date), policy);
            return true;
        }
        catch (DomainValidationException)
        {
            return false;
        }
    }
}
