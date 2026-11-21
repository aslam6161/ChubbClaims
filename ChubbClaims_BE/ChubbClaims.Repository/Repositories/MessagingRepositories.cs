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

/// <summary>
/// Runs a consumer's side effect at most once per (consumer group, event id): the duplicate check,
/// the side effect and the ProcessedEvents row commit in one transaction. Kafka delivers at least
/// once; this makes the effect exactly once.
/// </summary>
internal static class ProcessedEventGuard
{
    public static Task<bool> RunOnceAsync(
        DataContext db, string consumerGroup, Guid eventId, DateTimeOffset now, Func<Task> sideEffect, CancellationToken ct)
    {
        // The retrying execution strategy (EnableRetryOnFailure) requires user transactions to run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from a clean slate
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            if (await db.ProcessedEvents.AnyAsync(p => p.ConsumerGroup == consumerGroup && p.EventId == eventId, ct))
                return false; // duplicate delivery: already applied

            await sideEffect();

            // The primary key (group, event) is the final guard: a concurrent duplicate fails here and is retried as a duplicate.
            db.ProcessedEvents.Add(new ProcessedEvent(consumerGroup, eventId, now));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        });
    }
}

public sealed class ExposureRepository(DataContext db, TimeProvider clock) : IExposureRepository
{
    public Task<bool> ApplyOnceAsync(
        string consumerGroup, Guid eventId, ExposureKey key, ExposureDelta delta, DateTimeOffset eventAt, CancellationToken ct) =>
        ProcessedEventGuard.RunOnceAsync(db, consumerGroup, eventId, clock.GetUtcNow(), async () =>
        {
            // Atomic increment in SQL (no read-modify-write), so two projector instances cannot lose an update.
            var updated = await db.ExposureByMarket
                .Where(e => e.Market == key.Market && e.Currency == key.Currency && e.ClaimType == key.ClaimType)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.OpenClaims, e => e.OpenClaims + delta.OpenClaims)
                    .SetProperty(e => e.OutstandingReserve, e => e.OutstandingReserve + delta.OutstandingReserve)
                    .SetProperty(e => e.LastEventAt, e => e.LastEventAt > eventAt ? e.LastEventAt : eventAt), ct);

            if (updated == 0) // first event for this market/currency/type
                db.ExposureByMarket.Add(new ExposureByMarket(key.Market, key.Currency, key.ClaimType, delta.OpenClaims, delta.OutstandingReserve, eventAt));
        }, ct);

    public async Task<IReadOnlyList<ExposureByMarket>> GetAllAsync(CancellationToken ct) =>
        await db.ExposureByMarket.AsNoTracking()
            .OrderBy(e => e.Market).ThenBy(e => e.Currency).ThenBy(e => e.ClaimType)
            .ToListAsync(ct);
}

public sealed class NotificationRepository(DataContext db, TimeProvider clock) : INotificationRepository
{
    public Task<bool> AddOnceAsync(string consumerGroup, Guid eventId, IReadOnlyList<Notification> notifications, CancellationToken ct) =>
        ProcessedEventGuard.RunOnceAsync(db, consumerGroup, eventId, clock.GetUtcNow(), () =>
        {
            db.Notifications.AddRange(notifications); // may be empty: the event is still recorded as processed
            return Task.CompletedTask;
        }, ct);

    public async Task<IReadOnlyList<Notification>> GetForUserAsync(Guid userId, int take, CancellationToken ct) =>
        await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    public Task<Notification?> GetAsync(Guid id, CancellationToken ct) =>
        db.Notifications.SingleOrDefaultAsync(n => n.Id == id, ct);

    public Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset at, CancellationToken ct) =>
        db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, at), ct);
}
