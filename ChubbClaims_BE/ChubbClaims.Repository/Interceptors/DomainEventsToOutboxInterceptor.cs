using ChubbClaims.Domain.Entity;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Repository.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ChubbClaims.Repository.Interceptors;

/// <summary>
/// Just before SaveChanges, moves the domain events collected on tracked claims into
/// <see cref="OutboxMessage"/> rows. They are inserted by the same SaveChanges call, so the claim
/// change and its events commit or roll back together (no dual write to DB and Kafka).
/// Nothing is published here; the outbox publisher does that later.
/// </summary>
public sealed class DomainEventsToOutboxInterceptor(ICorrelationContext correlation) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteOutbox(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        WriteOutbox(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void WriteOutbox(DbContext? context)
    {
        if (context is null) return;

        var claimsWithEvents = context.ChangeTracker.Entries<Claim>()
            .Select(entry => entry.Entity)
            .Where(claim => claim.DomainEvents.Count > 0)
            .ToList();

        foreach (var claim in claimsWithEvents)
        {
            foreach (var domainEvent in claim.DomainEvents)
                context.Set<OutboxMessage>().Add(OutboxMessageFactory.Create(domainEvent, claim, correlation.CorrelationId));

            // Cleared now so a second SaveChanges on the same context cannot write them twice.
            // If this SaveChanges fails the request fails and the scoped context is discarded.
            claim.ClearDomainEvents();
        }
    }
}
