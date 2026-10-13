using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillStreamPageRowConfiguration : IEntityTypeConfiguration<BillStreamPageRow>
{
    public void Configure(EntityTypeBuilder<BillStreamPageRow> builder)
    {
        builder.ToTable("bill_stream_pages", table =>
            table.HasCheckConstraint("ck_bill_stream_pages_recorded", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999"));
        builder.Property(row => row.ListJobId).HasColumnName("list_job_id").HasMaxLength(128);
        builder.Property(row => row.RecordedAtUtcTicks).HasColumnName("recorded_at_utc_ticks");
        builder.HasKey(row => row.ListJobId);
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.ListJobId).OnDelete(DeleteBehavior.Restrict);
    }
}
