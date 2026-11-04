using CivicLens.Core.Briefs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefRevisionParagraphRowConfiguration : IEntityTypeConfiguration<BriefRevisionParagraphRow>
{
    public void Configure(EntityTypeBuilder<BriefRevisionParagraphRow> builder)
    {
        builder.ToTable("brief_revision_paragraphs", table =>
        {
            table.HasCheckConstraint("ck_brief_revision_paragraphs_part", EnumChecks.InList("part", Enum.GetNames<BriefRevisionPart>()));
            table.HasCheckConstraint("ck_brief_revision_paragraphs_position", $"position BETWEEN 0 AND {BriefRevision.MaximumParagraphs - 1}");
            table.HasCheckConstraint("ck_brief_revision_paragraphs_text", $"length(btrim(paragraph_text)) BETWEEN 1 AND {BriefRevision.MaximumParagraphLength}");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Part, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(BriefRevision.MaximumRecordIdLength);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Part).HasColumnName("part").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.ParagraphText).HasColumnName("paragraph_text").IsRequired();
        builder.HasMany(row => row.Citations).WithOne()
            .HasForeignKey(citation => new { citation.RecordId, citation.RevisionNumber, citation.Part, citation.ParagraphPosition })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
