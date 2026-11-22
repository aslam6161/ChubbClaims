using ChubbClaims.Domain.Entity;
using ChubbClaims.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChubbClaims.Repository.Configurations;

public sealed class InformationRequestConfiguration : IEntityTypeConfiguration<InformationRequest>
{
    public void Configure(EntityTypeBuilder<InformationRequest> b)
    {
        b.ToTable("InformationRequests");
        b.Property(r => r.Question).HasMaxLength(2000).IsRequired();
        b.Property(r => r.Response).HasMaxLength(4000);
        b.Ignore(r => r.IsOpen);
    }
}

public sealed class ClaimStatusChangeConfiguration : IEntityTypeConfiguration<ClaimStatusChange>
{
    public void Configure(EntityTypeBuilder<ClaimStatusChange> b)
    {
        b.ToTable("ClaimStatusHistory");
        b.Property(h => h.Reason).HasMaxLength(2000);
        b.HasIndex(h => new { h.ClaimId, h.At });
    }
}

public sealed class ReserveChangeConfiguration : IEntityTypeConfiguration<ReserveChange>
{
    public void Configure(EntityTypeBuilder<ReserveChange> b)
    {
        b.ToTable("ClaimReserveHistory");
        b.Property(h => h.Reason).HasMaxLength(2000).IsRequired();
        b.HasIndex(h => new { h.ClaimId, h.At });
    }
}

public sealed class ClaimNoteConfiguration : IEntityTypeConfiguration<ClaimNote>
{
    public void Configure(EntityTypeBuilder<ClaimNote> b)
    {
        b.ToTable("ClaimNotes");
        b.Property(n => n.Text).HasMaxLength(4000).IsRequired();
    }
}

public sealed class ClaimDocumentConfiguration : IEntityTypeConfiguration<ClaimDocument>
{
    public void Configure(EntityTypeBuilder<ClaimDocument> b)
    {
        b.ToTable("ClaimDocuments");
        b.Property(d => d.StorageKey).HasMaxLength(200).IsRequired();
        b.HasIndex(d => d.StorageKey).IsUnique();
        b.Property(d => d.FileName).HasMaxLength(ClaimDocumentRules.MaxFileNameLength).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(50).IsRequired();
        b.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        b.HasIndex(d => new { d.ClaimId, d.UploadedAt });
    }
}
