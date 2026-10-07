using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class FeedCandidateJobRowConfiguration : IEntityTypeConfiguration<FeedCandidateJobRow>
{
    public void Configure(EntityTypeBuilder<FeedCandidateJobRow> builder)
    {
        builder.ToTable("collection_feed_candidate_jobs");
        builder.HasKey(row => row.CandidateHash);
        builder.Property(row => row.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(row => row.CandidateHash).HasColumnName("candidate_hash").HasMaxLength(64);
        builder.Property(row => row.Url).HasColumnName("url").HasMaxLength(4096).IsRequired();
        builder.Property(row => row.JobId).HasColumnName("job_id").HasMaxLength(32).IsRequired();
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
