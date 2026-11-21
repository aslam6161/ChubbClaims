namespace ChubbClaims.Domain.Entity;

public abstract class BaseEntity
{
    /// <summary>Generated in the domain (time-ordered v7) so an entity has its identity before it is saved.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
