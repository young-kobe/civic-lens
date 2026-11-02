using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisRowConfiguration : IEntityTypeConfiguration<DocumentChangeAnalysisRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue<DocumentChangeAnalysisRow, DocumentChangeAnalysisRunRow>(builder, "document_change_analysis", nameof(DocumentChangeAnalysisRow.ComparisonId),
            "comparison_id");
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.HasOne<Documents.Persistence.DocumentComparisonRow>().WithMany()
            .HasForeignKey(row => row.ComparisonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Review.Persistence.DocumentChangeDraftRow>().WithMany()
            .HasForeignKey(row => row.DraftId).OnDelete(DeleteBehavior.Restrict);
    }
}
