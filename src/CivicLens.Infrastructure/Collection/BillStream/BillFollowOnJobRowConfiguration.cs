using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillFollowOnJobRowConfiguration : IEntityTypeConfiguration<BillFollowOnJobRow>
{
    public void Configure(EntityTypeBuilder<BillFollowOnJobRow> builder)
    {
        builder.ToTable("bill_follow_on_jobs");
        builder.Property(row => row.JobId).HasColumnName("job_id").HasMaxLength(128);
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.HasKey(row => row.JobId);
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillGateRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
    }
}
