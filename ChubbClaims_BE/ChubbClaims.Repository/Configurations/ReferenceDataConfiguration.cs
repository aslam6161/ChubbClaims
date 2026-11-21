using ChubbClaims.Domain.Entity;
using ChubbClaims.Repository.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChubbClaims.Repository.Configurations;

public sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> b)
    {
        b.ToTable("Policies");
        b.HasKey(p => p.PolicyNumber);
        b.Property(p => p.PolicyNumber).HasMaxLength(32);
        b.Property(p => p.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.HasIndex(p => p.HolderUserId);
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.Property(u => u.Name).HasMaxLength(200).IsRequired();
        b.Property(u => u.Email).HasMaxLength(256).IsRequired();
        b.HasIndex(u => u.Email).IsUnique();
    }
}

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.EventType).HasMaxLength(64).IsRequired();
        b.Property(m => m.Payload).IsRequired();
        b.Property(m => m.CorrelationId).HasMaxLength(64);
        b.Property(m => m.LastError).HasMaxLength(2000);

        // The publisher polls "unpublished, oldest first".
        b.HasIndex(m => new { m.ProcessedAt, m.OccurredAt });
    }
}
