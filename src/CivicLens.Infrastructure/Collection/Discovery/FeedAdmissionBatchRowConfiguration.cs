using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Discovery;

internal sealed class FeedAdmissionBatchRowConfiguration : IEntityTypeConfiguration<FeedAdmissionBatchRow>
{
    public void Configure(EntityTypeBuilder<FeedAdmissionBatchRow> builder)
    {
        builder.ToTable("collection_feed_admission_batches");
        builder.HasKey(row => row.IdempotencyKey);
        builder.Property(row => row.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").HasMaxLength(128).IsRequired();
        builder.Property(row => row.InputJson).HasColumnName("input_json").IsRequired();
        builder.Property(row => row.ResultJson).HasColumnName("result_json").IsRequired();
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.HasOne<FeedDiscoveryRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
