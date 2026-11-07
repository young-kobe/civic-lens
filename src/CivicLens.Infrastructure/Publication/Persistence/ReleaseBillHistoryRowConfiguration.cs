using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseBillHistoryRowConfiguration : IEntityTypeConfiguration<ReleaseBillHistoryRow>
{
    public void Configure(EntityTypeBuilder<ReleaseBillHistoryRow> builder)
    {
        builder.ToTable("release_bill_histories", table =>
            table.HasCheckConstraint("ck_release_bill_histories_record_id", "record_id ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(row => new { row.ReleaseNumber, row.RecordId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedBillHistoryRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
    }
}
