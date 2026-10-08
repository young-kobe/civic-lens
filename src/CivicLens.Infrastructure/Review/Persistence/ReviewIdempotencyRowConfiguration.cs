using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class ReviewIdempotencyRowConfiguration : IEntityTypeConfiguration<ReviewIdempotencyRow>
{
    public void Configure(EntityTypeBuilder<ReviewIdempotencyRow> builder)
    {
        builder.ToTable("document_change_review_idempotency", table =>
            table.HasCheckConstraint("ck_document_change_review_idempotency_hash", "payload_hash ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(row => new { row.ActorSubject, row.Operation, row.IdempotencyKey });
        builder.Property(row => row.ActorSubject).HasColumnName("actor_subject").HasMaxLength(256);
        builder.Property(row => row.Operation).HasColumnName("operation").HasMaxLength(32);
        builder.Property(row => row.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
        builder.Property(row => row.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.ResultJson).HasColumnName("result_json").IsRequired();
    }
}
