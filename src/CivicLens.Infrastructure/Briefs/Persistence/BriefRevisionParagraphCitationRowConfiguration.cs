using CivicLens.Core.Briefs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefRevisionParagraphCitationRowConfiguration : IEntityTypeConfiguration<BriefRevisionParagraphCitationRow>
{
    public void Configure(EntityTypeBuilder<BriefRevisionParagraphCitationRow> builder)
    {
        builder.ToTable("brief_revision_paragraph_citations", table =>
        {
            table.HasCheckConstraint("ck_brief_revision_paragraph_citations_position", $"position BETWEEN 0 AND {BriefRevision.MaximumCitations - 1}");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Part, row.ParagraphPosition, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(BriefRevision.MaximumRecordIdLength);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Part).HasColumnName("part").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.ParagraphPosition).HasColumnName("paragraph_position");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.CitationId).HasColumnName("citation_id").HasMaxLength(8);
        builder.HasIndex(row => new { row.RecordId, row.RevisionNumber, row.Part, row.ParagraphPosition, row.CitationId }).IsUnique();
        builder.HasOne<BriefRevisionCitationRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.RevisionNumber, row.Part, row.CitationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
