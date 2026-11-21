using ChubbClaims.Domain.Events;

namespace ChubbClaims.Domain.Entity;

/// <summary>
/// An entity that is a consistency boundary. Collects domain events while it changes; the
/// persistence layer moves them into the outbox inside the same SaveChanges transaction.
/// </summary>
public abstract class AggregateRoot : BaseEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
