using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillTextVersionRowConfiguration : IEntityTypeConfiguration<BillTextVersionRow>
{
    public void Configure(EntityTypeBuilder<BillTextVersionRow> builder)
    {
        builder.ToTable("bill_text_versions", table =>
        {
            table.HasCheckConstraint("ck_bill_text_versions_bill", "congress >= 1 AND bill_number >= 1");
            table.HasCheckConstraint("ck_bill_text_versions_package", "package_id ~ '^BILLS-[0-9]+[a-z]+[0-9]+[a-z]+$'");
            table.HasCheckConstraint("ck_bill_text_versions_type", "version_type = btrim(version_type) AND char_length(version_type) >= 1");
            table.HasCheckConstraint("ck_bill_text_versions_chars", "chars_total >= 0 AND chars_read >= 0 AND chars_read <= chars_total");
            table.HasCheckConstraint("ck_bill_text_versions_hash", "content_sha256 ~ '^[0-9a-f]{64}$'");
        });
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.PackageId).HasColumnName("package_id").HasMaxLength(64);
        builder.Property(row => row.VersionType).HasColumnName("version_type").HasMaxLength(CongressBillTextVersion.MaximumTypeLength);
        builder.Property(row => row.VersionDate).HasColumnName("version_date");
        builder.Property(row => row.SourceUrl).HasColumnName("source_url").HasMaxLength(CongressBillTextVersion.MaximumUrlLength);
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64);
        builder.Property(row => row.CharsTotal).HasColumnName("chars_total");
        builder.Property(row => row.CharsRead).HasColumnName("chars_read");
        builder.Property(row => row.ContentSha256).HasColumnName("content_sha256").HasMaxLength(64);
        builder.HasKey(row => new { row.Congress, row.BillType, row.BillNumber, row.PackageId });
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.ExtractionId).OnDelete(DeleteBehavior.Restrict);
    }
}
