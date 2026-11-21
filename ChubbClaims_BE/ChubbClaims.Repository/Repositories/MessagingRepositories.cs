using ChubbClaims.Domain.ReadModels;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Repository.Outbox;
using Microsoft.EntityFrameworkCore;

namespace ChubbClaims.Repository.Repositories;

public sealed class OutboxRepository(DataContext db) : IOutboxRepository
{
    public async Task<IReadOnlyList<OutboxMessage>> GetUnpublishedAsync(int take, CancellationToken ct) =>
        await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.Sequence)
            .Take(take)
            .ToListAsync(ct);
}

public sealed class ExposureRepository(DataContext db, TimeProvider clock) : IExposureRepository
{
    public async Task<bool> ApplyOnceAsync(
        string consumerGroup, Guid eventId, ExposureKey key, ExposureDelta delta, DateTimeOffset eventAt, CancellationToken ct)
    {
        // The retrying execution strategy (EnableRetryOnFailure) requires user transactions to run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from a clean slate
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            if (await db.ProcessedEvents.AnyAsync(p => p.ConsumerGroup == consumerGroup && p.EventId == eventId, ct))
                return false; // duplicate delivery: already applied

            // Atomic increment in SQL (no read-modify-write), so two projector instances cannot lose an update.
            var updated = await db.ExposureByMarket
                .Where(e => e.Market == key.Market && e.Currency == key.Currency && e.ClaimType == key.ClaimType)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.OpenClaims, e => e.OpenClaims + delta.OpenClaims)
                    .SetProperty(e => e.OutstandingReserve, e => e.OutstandingReserve + delta.OutstandingReserve)
                    .SetProperty(e => e.LastEventAt, e => e.LastEventAt > eventAt ? e.LastEventAt : eventAt), ct);

            if (updated == 0) // first event for this market/currency/type
                db.ExposureByMarket.Add(new ExposureByMarket(key.Market, key.Currency, key.ClaimType, delta.OpenClaims, delta.OutstandingReserve, eventAt));

            // The primary key (group, event) is the final guard: a concurrent duplicate fails here and is retried as a duplicate.
            db.ProcessedEvents.Add(new ProcessedEvent(consumerGroup, eventId, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }

    public async Task<IReadOnlyList<ExposureByMarket>> GetAllAsync(CancellationToken ct) =>
        await db.ExposureByMarket.AsNoTracking()
            .OrderBy(e => e.Market).ThenBy(e => e.Currency).ThenBy(e => e.ClaimType)
            .ToListAsync(ct);
}
