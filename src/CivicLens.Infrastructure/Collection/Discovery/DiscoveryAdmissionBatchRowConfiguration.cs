using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class DiscoveryAdmissionBatchRowConfiguration : IEntityTypeConfiguration<DiscoveryAdmissionBatchRow>
{
    public void Configure(EntityTypeBuilder<DiscoveryAdmissionBatchRow> builder)
    {
        builder.ToTable("collection_admission_batches");
        builder.HasKey(row => row.IdempotencyKey);
        builder.Property(row => row.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").HasMaxLength(128).IsRequired();
        builder.Property(row => row.InputJson).HasColumnName("input_json").IsRequired();
        builder.Property(row => row.ConfigurationRevisionId).HasColumnName("configuration_revision_id").HasMaxLength(64);
        builder.Property(row => row.ResultJson).HasColumnName("result_json").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.HasOne<DiscoveryRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionRow>().WithMany().HasForeignKey(row => row.ConfigurationRevisionId)
            .HasConstraintName("fk_collection_admission_batches_configuration_revision").OnDelete(DeleteBehavior.Restrict);
    }
}
