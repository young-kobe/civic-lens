using CivicLens.Application.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Spending.Persistence;

internal sealed class AiModelPriceRevisionRowConfiguration : IEntityTypeConfiguration<AiModelPriceRevisionRow>
{
    public void Configure(EntityTypeBuilder<AiModelPriceRevisionRow> builder)
    {
        builder.ToTable("ai_model_price_revisions", table =>
        {
            table.HasCheckConstraint("ck_ai_model_price_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_ai_model_price_revisions_prices",
                $"input_micros BETWEEN 0 AND {ModelPrice.MaximumMicros} AND output_micros BETWEEN 0 AND {ModelPrice.MaximumMicros} AND " +
                $"cache_read_micros BETWEEN 0 AND {ModelPrice.MaximumMicros} AND cache_write_micros BETWEEN 0 AND {ModelPrice.MaximumMicros}");
        });
        builder.HasKey(row => new { row.Model, row.RevisionNumber });
        builder.Property(row => row.Model).HasColumnName("model").HasMaxLength(128);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number").ValueGeneratedNever();
        builder.Property(row => row.InputMicros).HasColumnName("input_micros");
        builder.Property(row => row.OutputMicros).HasColumnName("output_micros");
        builder.Property(row => row.CacheReadMicros).HasColumnName("cache_read_micros");
        builder.Property(row => row.CacheWriteMicros).HasColumnName("cache_write_micros");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasIndex(row => row.CreatedAtUtcTicks);
    }
}
