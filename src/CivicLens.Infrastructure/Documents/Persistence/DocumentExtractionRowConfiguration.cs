using CivicLens.Infrastructure.Collection.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentExtractionRowConfiguration : IEntityTypeConfiguration<DocumentExtractionRow>
{
    public void Configure(EntityTypeBuilder<DocumentExtractionRow> builder)
    {
        builder.ToTable("document_extractions", table =>
        {
            table.HasCheckConstraint("ck_document_extractions_hashes",
                "extraction_id ~ '^[0-9a-f]{64}$' AND text_sha256 ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_document_extractions_versions",
                "length(btrim(parser_version)) > 0 AND length(btrim(normalization_version)) > 0");
            table.HasCheckConstraint("ck_document_extractions_text_limit", "length(text) <= 2000000");
        });
        builder.HasKey(row => row.ExtractionId);
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.ParserVersion).HasColumnName("parser_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.NormalizationVersion).HasColumnName("normalization_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.Text).HasColumnName("text").IsRequired();
        builder.Property(row => row.TextSha256).HasColumnName("text_sha256").HasMaxLength(64).IsRequired();
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.AttemptId).HasDatabaseName("ix_document_extractions_attempt");
    }
}
