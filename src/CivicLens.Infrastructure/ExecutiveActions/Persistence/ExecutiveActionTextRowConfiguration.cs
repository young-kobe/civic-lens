using CivicLens.Application.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionTextRowConfiguration : IEntityTypeConfiguration<ExecutiveActionTextRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionTextRow> builder)
    {
        builder.ToTable("executive_action_texts", table =>
            table.HasCheckConstraint("ck_executive_action_texts_chars",
                $"chars_read >= 1 AND chars_read <= {TextWindow.Length} AND chars_read <= chars_total AND " +
                $"(chars_read = chars_total OR chars_total > {TextWindow.Length})"));
        builder.HasKey(row => new { row.SourceId, row.DocumentNumber });
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.DocumentNumber).HasColumnName("document_number").HasMaxLength(32);
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.CharsTotal).HasColumnName("chars_total");
        builder.Property(row => row.CharsRead).HasColumnName("chars_read");
        builder.HasOne<ExecutiveActionDocumentRow>().WithOne()
            .HasForeignKey<ExecutiveActionTextRow>(row => new { row.SourceId, row.DocumentNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.ExtractionId).OnDelete(DeleteBehavior.Restrict);
    }
}
