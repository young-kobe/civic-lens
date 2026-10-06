using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class CollectorSlotRowConfiguration : IEntityTypeConfiguration<CollectorSlotRow>
{
    public void Configure(EntityTypeBuilder<CollectorSlotRow> builder)
    {
        builder.ToTable("collection_collector_slot", table =>
        {
            table.HasCheckConstraint("ck_collector_singleton", "id = 1");
            table.HasCheckConstraint("ck_collector_lease", "(token IS NULL AND expires_at IS NULL AND job_id IS NULL AND attempt_id IS NULL) OR (token IS NOT NULL AND expires_at IS NOT NULL AND job_id IS NOT NULL AND attempt_id IS NOT NULL)");
        });
        builder.Property(row => row.Id).HasColumnName("id");
        builder.Property(row => row.JobId).HasColumnName("job_id");
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.Property(row => row.Token).HasColumnName("token");
        builder.Property(row => row.Fence).HasColumnName("fence");
        builder.Property(row => row.ExpiresAt).HasColumnName("expires_at");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobAttemptRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
