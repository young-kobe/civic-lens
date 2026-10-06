using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Persistence;

internal sealed class CaptureRowConfiguration : IEntityTypeConfiguration<CaptureRow>
{
    public void Configure(EntityTypeBuilder<CaptureRow> builder)
    {
        builder.ToTable("collection_captures", table =>
        {
            table.HasCheckConstraint("ck_collection_captures_byte_length", "byte_length >= 0");
            table.HasCheckConstraint("ck_collection_captures_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(row => row.Sha256);
        builder.Property(row => row.Sha256).HasColumnName("sha256").IsRequired();
        builder.Property(row => row.ByteLength).HasColumnName("byte_length").IsRequired();
    }
}
