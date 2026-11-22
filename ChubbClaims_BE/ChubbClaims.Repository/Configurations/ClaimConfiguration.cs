using ChubbClaims.Domain.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChubbClaims.Repository.Configurations;

public sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Claim> b)
    {
        b.ToTable("Claims");
        b.HasKey(c => c.Id);

        b.Property(c => c.ClaimNumber).HasMaxLength(32).IsRequired();
        b.HasIndex(c => c.ClaimNumber).IsUnique();
        b.Property(c => c.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(c => c.PolicyNumber).HasMaxLength(32).IsRequired();
        b.Property(c => c.IncidentLocation).HasMaxLength(500).IsRequired();
        b.Property(c => c.Description).HasMaxLength(4000).IsRequired();

        // Type-specific details as owned types: columns on the Claims table (no join, no JSON
        // parsing in queries), and only one of the two is ever set. A JSON column would suit
        // many optional types better; with two small fixed shapes, columns are simpler.
        b.OwnsOne(c => c.MotorDetails, m =>
        {
            m.Property(x => x.VehicleRegistration).HasColumnName("Motor_VehicleRegistration").HasMaxLength(20).IsRequired();
            m.Property(x => x.ThirdPartyInvolved).HasColumnName("Motor_ThirdPartyInvolved");
            m.Property(x => x.PoliceReportNumber).HasColumnName("Motor_PoliceReportNumber").HasMaxLength(50);
        });
        b.OwnsOne(c => c.PropertyDetails, p =>
        {
            p.Property(x => x.PropertyAddress).HasColumnName("Property_Address").HasMaxLength(500).IsRequired();
            p.Property(x => x.DamageType).HasColumnName("Property_DamageType");
        });

        // Optimistic concurrency: two officers acting on the same claim -> the second save fails (409).
        // A shadow property keeps this persistence concern out of the domain model.
        b.Property<byte[]>(RowVersion).IsRowVersion();

        // Children are owned by the aggregate; EF writes through the private backing lists.
        b.HasMany(c => c.InformationRequests).WithOne().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.StatusHistory).WithOne().HasForeignKey(h => h.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.ReserveHistory).WithOne().HasForeignKey(h => h.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Notes).WithOne().HasForeignKey(n => n.ClaimId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(c => c.Documents).WithOne().HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Cascade);
        foreach (var navigation in new[] { nameof(Claim.InformationRequests), nameof(Claim.StatusHistory), nameof(Claim.ReserveHistory), nameof(Claim.Notes), nameof(Claim.Documents) })
            b.Navigation(navigation).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(c => c.DomainEvents);
        b.Ignore(c => c.IsClosed);
        b.Ignore(c => c.OutstandingReserve);
        b.Ignore(c => c.OpenInformationRequest);

        // Policy and users belong to other systems in reality: referenced by id/number, no FK.

        // Work queue: unassigned claims in my market / my claims by status; claimant: my claims; SLA sort.
        b.HasIndex(c => new { c.Status, c.Market });
        b.HasIndex(c => new { c.AssignedOfficerId, c.Status });
        b.HasIndex(c => c.ClaimantId);
        b.HasIndex(c => c.SlaDueAt);
    }
}
