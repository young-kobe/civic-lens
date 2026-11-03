using CivicLens.Core.Review;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionCitationRowConfiguration : IEntityTypeConfiguration<DocumentChangeRevisionCitationRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeRevisionCitationRow> builder)
    {
        builder.ToTable("document_change_revision_citations", table =>
        {
            table.HasCheckConstraint("ck_document_change_revision_citations_position", $"position BETWEEN 0 AND {DocumentChangeDraftRevision.MaximumCitations - 1}");
            table.HasCheckConstraint("ck_document_change_revision_citations_length", $"citation_length <= {DocumentChangeDraftRevision.MaximumCitationLength}");
        });
        builder.HasKey(row => new { row.DraftId, row.RevisionNumber, row.Position });
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        SourceSpanColumns.Configure(builder, "document_change_revision_citations", "Citation", "citation", optional: false);
        builder.HasIndex(row => new { row.DraftId, row.RevisionNumber, row.CitationExtractionId, row.CitationStart, row.CitationLength }).IsUnique();
    }
}
