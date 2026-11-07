using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedBillHistoryRowConfiguration : IEntityTypeConfiguration<PublishedBillHistoryRow>
{
    public void Configure(EntityTypeBuilder<PublishedBillHistoryRow> builder)
    {
        builder.ToTable("published_bill_histories", table =>
        {
            table.HasCheckConstraint("ck_published_bill_histories_congress", "congress >= 1");
            table.HasCheckConstraint("ck_published_bill_histories_bill_number", "bill_number >= 1");
            table.HasCheckConstraint("ck_published_bill_histories_action_count", "action_count >= 0");
        });
        builder.HasKey(row => row.DetailJobId);
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.ObservedAtUtcTicks).HasColumnName("observed_at_utc_ticks");
        builder.Property(row => row.ActionCount).HasColumnName("action_count");
        builder.Property(row => row.PublicLaw).HasColumnName("public_law");
        builder.HasOne<BillGateRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
    }
}
