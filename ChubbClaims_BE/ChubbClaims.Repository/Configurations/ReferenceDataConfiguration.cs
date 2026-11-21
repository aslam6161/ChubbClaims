using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.ReadModels;
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
        b.Property(m => m.Sequence).UseIdentityColumn();

        // The publisher polls "unpublished, in insertion order".
        b.HasIndex(m => new { m.ProcessedAt, m.Sequence });
    }
}

public sealed class ExposureByMarketConfiguration : IEntityTypeConfiguration<ExposureByMarket>
{
    public void Configure(EntityTypeBuilder<ExposureByMarket> b)
    {
        b.ToTable("ExposureByMarket");
        b.HasKey(e => new { e.Market, e.Currency, e.ClaimType });
        b.Property(e => e.Currency).HasMaxLength(3).IsFixedLength();
    }
}

public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> b)
    {
        b.ToTable("ProcessedEvents");
        // The primary key is the idempotency guarantee: one row per (consumer group, event).
        b.HasKey(e => new { e.ConsumerGroup, e.EventId });
        b.Property(e => e.ConsumerGroup).HasMaxLength(100);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(n => n.Id);
        b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.ClaimNumber).HasMaxLength(32).IsRequired();
        b.Property(n => n.Message).HasMaxLength(Notification.MaxMessageLength).IsRequired();
        b.Ignore(n => n.IsRead);

        // "My latest notifications" and "my unread count".
        b.HasIndex(n => new { n.UserId, n.CreatedAt });
    }
}
