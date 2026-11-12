using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialBillCountRowConfiguration : IEntityTypeConfiguration<ReleaseOfficialBillCountRow>
{
    public void Configure(EntityTypeBuilder<ReleaseOfficialBillCountRow> builder)
    {
        builder.ToTable("release_official_bill_counts", table =>
        {
            table.HasCheckConstraint("ck_release_official_bill_counts_congress", "congress > 0");
            table.HasCheckConstraint("ck_release_official_bill_counts_official_id", "length(btrim(official_id)) > 0");
            table.HasCheckConstraint("ck_release_official_bill_counts_sponsored", "sponsored >= 0");
            table.HasCheckConstraint("ck_release_official_bill_counts_cosponsored", "cosponsored >= 0");
            table.HasCheckConstraint("ck_release_official_bill_counts_nonzero", "sponsored + cosponsored > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.Congress, row.OfficialId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Sponsored).HasColumnName("sponsored");
        builder.Property(row => row.Cosponsored).HasColumnName("cosponsored");
        builder.HasOne<ReleaseBillCountRow>().WithMany()
            .HasForeignKey(row => new { row.ReleaseNumber, row.Congress }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_release_official_bill_counts_header");
        builder.HasOne<ReleaseOfficialRow>().WithMany()
            .HasForeignKey(row => new { row.ReleaseNumber, row.OfficialId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_release_official_bill_counts_official");
        builder.HasIndex(row => new { row.ReleaseNumber, row.OfficialId })
            .HasDatabaseName("ix_release_official_bill_counts_release_official");
    }
}
