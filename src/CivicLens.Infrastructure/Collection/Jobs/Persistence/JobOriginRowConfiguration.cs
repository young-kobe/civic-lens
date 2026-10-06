using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobOriginRowConfiguration : IEntityTypeConfiguration<JobOriginRow>
{
    public void Configure(EntityTypeBuilder<JobOriginRow> builder)
    {
        builder.ToTable("collection_job_origins");
        builder.Property(row => row.Origin).HasColumnName("origin");
        builder.Property(row => row.NotBefore).HasColumnName("not_before");
        builder.Property(row => row.UnresolvedAttemptId).HasColumnName("unresolved_attempt_id");
        builder.HasKey(row => row.Origin);
        builder.HasOne<JobAttemptRow>().WithMany().HasForeignKey(row => row.UnresolvedAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
