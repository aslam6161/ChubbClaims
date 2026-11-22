using System.Diagnostics;
using ChubbClaims.SmokeTests.Infrastructure;
using static ChubbClaims.SmokeTests.Infrastructure.Users;

namespace ChubbClaims.SmokeTests;

/// <summary>The exposure report is a read model fed from Kafka: updated shortly after a claim changes.</summary>
public class ExposureTests
{
    private sealed record Row(int Open, decimal Outstanding);

    private static async Task<(Dictionary<string, Row> Rows, JsonNode Report)> Read()
    {
        var report = await Api.Get("/api/reports/exposure", ManagerMy);
        report.Status.ShouldBe(HttpStatusCode.OK, report.ToString());
        var rows = report.Json.List("rows").ToDictionary(
            r => $"{r.Str("market")}/{r.Str("currency")}/{r.Str("claimType")}",
            r => new Row(r.Int("openClaims"), r.Num("outstandingReserve")));
        return (rows, report.Json);
    }

    [SmokeFact]
    public async Task Officers_cannot_read_exposure() =>
        (await Api.Get("/api/reports/exposure", OfficerMy)).Status.ShouldBe(HttpStatusCode.Forbidden);

    [SmokeFact]
    public async Task Totals_are_per_currency_never_summed_across_currencies()
    {
        var (_, report) = await Read();
        report.List("totalsByCurrency").Select(t => t.Str("currency")).Order()
            .ShouldBe(new[] { "AUD", "HKD", "IDR", "MYR", "SGD", "THB" });
    }

    [SmokeFact]
    public async Task An_FNOL_is_accepted_at_once_and_reaches_exposure_through_Kafka_shortly_after()
    {
        const string key = "MY/MYR/Motor";
        var before = (await Api.Settled(Read, (a, b) => a.Rows.Count == b.Rows.Count && !a.Rows.Except(b.Rows).Any())).Rows[key];

        var clock = Stopwatch.StartNew();
        var claim = await Claims.Submit(estimate: 4_321.50m, description: "Exposure smoke test");
        var accepted = clock.Elapsed;

        var expected = new Row(before.Open + 1, before.Outstanding + 4_321.50m);
        var seen = await Api.WaitFor(async () => (await Read()).Rows[key] is var row && row == expected ? row : null);
        seen.ShouldNotBeNull($"MY/MYR/Motor should become {expected}; still {(await Read()).Rows[key]}");
        Api.Log($"FNOL answered in {accepted.TotalMilliseconds:F0} ms; exposure updated after {clock.Elapsed.TotalSeconds:F2} s");

        await Claims.Close(claim.Id);
    }
}
