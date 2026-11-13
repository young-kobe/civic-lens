using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseBillStatusRowConfiguration : IEntityTypeConfiguration<ReleaseBillStatusRow>
{
    public void Configure(EntityTypeBuilder<ReleaseBillStatusRow> builder)
    {
        builder.ToTable("release_bill_statuses", table =>
        {
            table.HasCheckConstraint("ck_release_bill_statuses_record_id", "record_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_release_bill_statuses_latest_action_text",
                $"length(btrim(latest_action_text)) > 0 AND length(latest_action_text) <= {PublishedBillStatus.MaximumTextLength}");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.RecordId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.LatestActionText).HasColumnName("latest_action_text").HasMaxLength(PublishedBillStatus.MaximumTextLength);
        builder.Property(row => row.LatestActionDate).HasColumnName("latest_action_date");
        builder.Property(row => row.ReadAtUtcTicks).HasColumnName("read_at_utc_ticks");
        builder.Property(row => row.Status).HasColumnName("status").HasConversion<string>();
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
