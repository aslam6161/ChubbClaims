using ChubbClaims.Domain.Constants;
using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Domain.Settings;
using ChubbClaims.Domain.ValueObjects;
using ChubbClaims.Repository.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChubbClaims.Repository.Data;

/// <summary>
/// Development seed data. Claims are created and progressed through the real domain methods,
/// so they obey every business rule and their events land in the outbox: the exposure read
/// model is then built by the real Kafka pipeline, not by a separate seed script.
/// Runs only when the database has no users.
/// </summary>
public sealed class DevDataSeeder(
    DataContext db, IClaimRepository claims, ClaimRulesSettings rules, TimeProvider clock, ILogger<DevDataSeeder> logger)
{
    private readonly Dictionary<Market, User> _claimants = [];
    private readonly Dictionary<(Market, ClaimType), Policy> _policies = [];
    private DateTimeOffset _now;

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogDebug("Seed skipped: database already has users");
            return;
        }

        _now = clock.GetUtcNow();
        var staff = SeedUsersAndPolicies();
        await SeedClaimsAsync(staff, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded {Users} users, {Policies} policies and {Claims} claims",
            db.Users.Local.Count, db.Policies.Local.Count, db.Claims.Local.Count);
    }

    private (User OfficerMy, User OfficerSg, User ManagerMy) SeedUsersAndPolicies()
    {
        var claimantNames = new Dictionary<Market, string>
        {
            [Market.MY] = "Aisyah Rahman", [Market.SG] = "Daniel Tan", [Market.HK] = "Wing Chan",
            [Market.TH] = "Somchai Prasert", [Market.ID] = "Putri Wijaya", [Market.AU] = "Olivia Brown",
        };
        foreach (var (market, name) in claimantNames)
        {
            var claimant = new User(name, $"claimant.{market.ToString().ToLowerInvariant()}@demo.local", UserRole.Claimant, null);
            _claimants[market] = claimant;
            db.Users.Add(claimant);

            foreach (var type in Enum.GetValues<ClaimType>())
            {
                var policy = new Policy(
                    $"POL-{market}-{(type == ClaimType.Motor ? "M" : "P")}-0001", claimant.Id, market, Markets.CurrencyOf(market), type,
                    Today(market).AddYears(-1), Today(market).AddYears(1));
                _policies[(market, type)] = policy;
                db.Policies.Add(policy);
            }
        }

        // Expired policy to demo FNOL validation ("policy was not in force").
        db.Policies.Add(new Policy("POL-MY-M-0000", _claimants[Market.MY].Id, Market.MY, "MYR", ClaimType.Motor,
            Today(Market.MY).AddYears(-2), Today(Market.MY).AddYears(-1).AddDays(-1)));

        var officerMy = new User("Hafiz Ismail", "officer.my@demo.local", UserRole.ClaimsOfficer, Market.MY);
        var officerSg = new User("Mei Ling Goh", "officer.sg@demo.local", UserRole.ClaimsOfficer, Market.SG);
        var managerMy = new User("Farah Abdullah", "manager.my@demo.local", UserRole.ClaimsManager, Market.MY);
        db.Users.AddRange(officerMy, officerSg, managerMy);
        return (officerMy, officerSg, managerMy);
    }

    private async Task SeedClaimsAsync((User OfficerMy, User OfficerSg, User ManagerMy) staff, CancellationToken ct)
    {
        var officerMy = staff.OfficerMy.AsActor();
        var officerSg = staff.OfficerSg.AsActor();
        var claimantMy = _claimants[Market.MY].AsActor();

        // MY: every status, one overdue, one awaiting the claimant.
        await Submit(Market.MY, ClaimType.Motor, daysAgo: 1, 8_500m, "Federal Highway, Petaling Jaya", "Side-swiped while changing lanes", ct);
        await Submit(Market.MY, ClaimType.Property, daysAgo: 14, null, "Bangsar, Kuala Lumpur", "Ceiling collapsed after heavy rain", ct, DamageType.Water);

        var underReview = await Submit(Market.MY, ClaimType.Motor, daysAgo: 6, 15_000m, "Jalan Tun Razak, Kuala Lumpur", "Rear-ended at a traffic light", ct);
        underReview.PickUp(officerMy, At(6, hours: 3));

        var awaiting = await Submit(Market.MY, ClaimType.Property, daysAgo: 5, 42_000m, "Mont Kiara, Kuala Lumpur", "Kitchen fire damaged cabinets and wiring", ct, DamageType.Fire);
        awaiting.PickUp(officerMy, At(5, hours: 2));
        awaiting.AdjustReserve(officerMy, 38_000m, "Contractor quote received", At(4));
        awaiting.RequestInformation(officerMy, "Please upload the fire department report and photos of the kitchen.", At(3));

        var approved = await Submit(Market.MY, ClaimType.Motor, daysAgo: 9, 6_200m, "Shah Alam", "Windscreen cracked by debris", ct);
        approved.PickUp(officerMy, At(9, hours: 4));
        approved.Approve(officerMy, 5_800m, "Agreed with panel workshop", At(2));

        var settled = await Submit(Market.MY, ClaimType.Motor, daysAgo: 20, 3_400m, "Cyberjaya", "Scratched in a car park", ct);
        settled.PickUp(officerMy, At(20, hours: 5));
        settled.RequestInformation(officerMy, "Please send photos of the scratches.", At(19));
        settled.ProvideInformation(claimantMy, settled.OpenInformationRequest!.Id, "Photos uploaded to the portal.", At(18));
        settled.Approve(officerMy, 3_100m, null, At(15));
        settled.Settle(officerMy, At(13));

        // SG: open, overdue under review, rejected.
        await Submit(Market.SG, ClaimType.Motor, daysAgo: 2, 4_800m, "Orchard Road", "Hit a bollard while parking", ct);

        var sgOverdue = await Submit(Market.SG, ClaimType.Property, daysAgo: 12, 25_000m, "Tampines", "Burst pipe flooded the flat below", ct, DamageType.Water);
        sgOverdue.PickUp(officerSg, At(11));

        var sgRejected = await Submit(Market.SG, ClaimType.Motor, daysAgo: 8, 12_000m, "Bukit Timah", "Engine damage after driving through flood", ct);
        sgRejected.PickUp(officerSg, At(8, hours: 6));
        sgRejected.Reject(officerSg, "Excluded: engine damage from driving into flood water", At(4));

        // Markets without staff in the demo: unassigned, but they count towards exposure.
        await Submit(Market.HK, ClaimType.Motor, daysAgo: 3, 30_000m, "Nathan Road, Kowloon", "Taxi collision at junction", ct);
        await Submit(Market.TH, ClaimType.Property, daysAgo: 4, 350_000m, "Sukhumvit, Bangkok", "Storm tore off part of the roof", ct, DamageType.Storm);
        await Submit(Market.ID, ClaimType.Motor, daysAgo: 2, null, "Jl. Sudirman, Jakarta", "Motorcycle hit the rear bumper", ct);
        await Submit(Market.AU, ClaimType.Property, daysAgo: 15, 18_000m, "Bondi, Sydney", "Break-in, electronics stolen", ct, DamageType.Theft);
    }

    private async Task<Claim> Submit(
        Market market, ClaimType type, int daysAgo, decimal? estimate, string location, string description, CancellationToken ct,
        DamageType damage = DamageType.Other)
    {
        var reportedAt = At(daysAgo);
        var incidentDate = Markets.LocalDate(market, reportedAt).AddDays(-1);
        var report = new IncidentReport(type, incidentDate, location, description, estimate,
            type == ClaimType.Motor ? new MotorDetails($"{market}-{1000 + daysAgo}", false, null) : null,
            type == ClaimType.Property ? new PropertyDetails(location, damage) : null);

        var number = ClaimNumber.Create(market, reportedAt.Year, await claims.NextClaimSequenceAsync(ct));
        var claim = Claim.Submit(report, _policies[(market, type)], _claimants[market].AsActor(), number, rules.For(market, type), reportedAt);
        claims.Add(claim);
        return claim;
    }

    private DateTimeOffset At(int daysAgo, int hours = 0) => _now.AddDays(-daysAgo).AddHours(hours);

    private DateOnly Today(Market market) => Markets.LocalDate(market, _now);
}
