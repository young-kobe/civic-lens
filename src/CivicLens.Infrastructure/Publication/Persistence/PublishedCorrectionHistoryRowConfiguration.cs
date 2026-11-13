using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedCorrectionHistoryRowConfiguration : IEntityTypeConfiguration<PublishedCorrectionHistoryRow>
{
    public void Configure(EntityTypeBuilder<PublishedCorrectionHistoryRow> builder)
    {
        builder.ToTable("published_correction_histories", table =>
        {
            table.HasCheckConstraint("ck_published_correction_histories_history_id", "history_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_published_correction_histories_encoding_version", "encoding_version = 1");
            table.HasCheckConstraint("ck_published_correction_histories_correction_count", $"correction_count BETWEEN 0 AND {PublishedCorrection.MaximumEntries}");
        });
        builder.HasKey(row => row.HistoryId);
        builder.Property(row => row.HistoryId).HasColumnName("history_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.EncodingVersion).HasColumnName("encoding_version").IsRequired();
        builder.Property(row => row.CorrectionCount).HasColumnName("correction_count").IsRequired();
    }
}
