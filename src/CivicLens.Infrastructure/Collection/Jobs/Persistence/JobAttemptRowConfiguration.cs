using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobAttemptRowConfiguration : IEntityTypeConfiguration<JobAttemptRow>
{
    public void Configure(EntityTypeBuilder<JobAttemptRow> builder)
    {
        builder.ToTable("collection_job_attempts", table =>
        {
            table.HasCheckConstraint("ck_job_attempt_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_job_attempt_resolution", "(resolution_json IS NULL) = (completed_at IS NULL)");
        });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.Property(row => row.JobId).HasColumnName("job_id");
        builder.Property(row => row.Sequence).HasColumnName("sequence");
        builder.Property(row => row.RequestJson).HasColumnName("request_json");
        builder.Property(row => row.StartedAt).HasColumnName("started_at");
        builder.Property(row => row.ResolutionJson).HasColumnName("resolution_json");
        builder.Property(row => row.CompletedAt).HasColumnName("completed_at");
        builder.HasKey(row => row.AttemptId);
        builder.Property(row => row.AttemptId).HasMaxLength(128);
        builder.Property(row => row.RequestJson).IsRequired();
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.JobId, row.Sequence }).IsUnique();
        builder.HasIndex(row => row.JobId).IsUnique().HasFilter("resolution_json IS NULL");
    }
}
