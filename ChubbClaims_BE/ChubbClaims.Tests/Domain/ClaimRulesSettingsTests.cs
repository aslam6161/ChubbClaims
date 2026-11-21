using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Settings;
using Shouldly;

namespace ChubbClaims.Tests.Domain;

public class ClaimRulesSettingsTests
{
    private static readonly ClaimRulesSettings Settings = new()
    {
        SlaDaysToDecision = 7,
        MaxIncidentAgeDays = 180,
        Markets =
        {
            [Market.MY] = new() { DefaultMotorReserve = 5_000m, DefaultPropertyReserve = 20_000m, HighPriorityThreshold = 50_000m },
            [Market.ID] = new() { DefaultMotorReserve = 20_000_000m, DefaultPropertyReserve = 80_000_000m, HighPriorityThreshold = 200_000_000m },
        },
    };

    [Fact]
    public void Default_reserve_depends_on_market_and_claim_type()
    {
        Settings.For(Market.MY, ClaimType.Motor).DefaultReserve.ShouldBe(5_000m);
        Settings.For(Market.MY, ClaimType.Property).DefaultReserve.ShouldBe(20_000m);
        Settings.For(Market.ID, ClaimType.Motor).DefaultReserve.ShouldBe(20_000_000m); // IDR, not MYR
    }

    [Fact]
    public void Global_sla_and_reporting_window_are_passed_through()
    {
        var rules = Settings.For(Market.ID, ClaimType.Property);

        rules.SlaDaysToDecision.ShouldBe(7);
        rules.MaxIncidentAgeDays.ShouldBe(180);
        rules.HighPriorityThreshold.ShouldBe(200_000_000m);
    }

    [Fact]
    public void Unconfigured_market_fails_loudly()
    {
        Should.Throw<InvalidOperationException>(() => Settings.For(Market.AU, ClaimType.Motor)).Message.ShouldContain("AU");
    }
}
