using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Enums;
using ChubbClaims.Repository.Outbox;
using Microsoft.EntityFrameworkCore;

namespace ChubbClaims.Repository;

public sealed class DataContext(DbContextOptions<DataContext> options) : DbContext(options)
{
    public const string ClaimNumberSequence = "ClaimNumberSequence";

    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<User> Users => Set<User>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Feeds the claim number (CLM-MY-2026-000123); a DB sequence is safe under concurrent FNOLs.
        modelBuilder.HasSequence<long>(ClaimNumberSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataContext).Assembly);

        // Ids are generated in the domain (Guid v7) before saving. Telling EF they are never
        // store-generated makes it INSERT new child entities found in a claim's collections
        // (otherwise a set key looks like an existing row and EF issues an UPDATE).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Property(nameof(BaseEntity.Id)).ValueGeneratedNever();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money: decimal(18,2) everywhere, never float.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Enums stored as readable strings: safe to reorder members and easy to query by hand.
        configurationBuilder.Properties<ClaimStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<ClaimType>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<Market>().HaveConversion<string>().HaveMaxLength(2);
        configurationBuilder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<Priority>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<DamageType>().HaveConversion<string>().HaveMaxLength(16);
    }
}
