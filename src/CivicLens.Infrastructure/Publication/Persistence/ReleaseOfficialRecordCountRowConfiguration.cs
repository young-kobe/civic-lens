using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialRecordCountRowConfiguration : IEntityTypeConfiguration<ReleaseOfficialRecordCountRow>
{
    public void Configure(EntityTypeBuilder<ReleaseOfficialRecordCountRow> builder)
    {
        builder.ToTable("release_official_record_counts", table =>
        {
            table.HasCheckConstraint("ck_release_official_record_counts_official_id", "length(btrim(official_id)) > 0");
            table.HasCheckConstraint("ck_release_official_record_counts_counts",
                "records >= 0 AND votes >= 0 AND bills >= 0 AND statements >= 0 AND executive_actions >= 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.OfficialId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Records).HasColumnName("records");
        builder.Property(row => row.Votes).HasColumnName("votes");
        builder.Property(row => row.Bills).HasColumnName("bills");
        builder.Property(row => row.Statements).HasColumnName("statements");
        builder.Property(row => row.ExecutiveActions).HasColumnName("executive_actions");
        builder.HasOne<ReleaseOfficialRow>().WithMany()
            .HasForeignKey(row => new { row.ReleaseNumber, row.OfficialId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_release_official_record_counts_official");
    }
}
