using CivicLens.Application.Briefs;
using CivicLens.Core.Briefs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefCheckFlagRowConfiguration : IEntityTypeConfiguration<BriefCheckFlagRow>
{
    public void Configure(EntityTypeBuilder<BriefCheckFlagRow> builder)
    {
        builder.ToTable("brief_check_flags", table =>
        {
            table.HasCheckConstraint("ck_brief_check_flags_number", $"flag_number BETWEEN 1 AND {BriefCheckResult.MaximumFlags}");
            table.HasCheckConstraint("ck_brief_check_flags_paragraph", $"paragraph_index BETWEEN 0 AND {BriefRevision.MaximumParagraphs - 1}");
            table.HasCheckConstraint("ck_brief_check_flags_code",
                "code IN (" + string.Join(",", Enum.GetNames<BriefCheckFlagCode>().Select(name => $"'{name}'")) + ")");
            table.HasCheckConstraint("ck_brief_check_flags_note", $"char_length(note) BETWEEN 1 AND {BriefCheckFlag.MaximumNoteLength}");
        });
        builder.HasKey(row => new { row.RunId, row.FlagNumber });
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.FlagNumber).HasColumnName("flag_number");
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.ParagraphIndex).HasColumnName("paragraph_index");
        builder.Property(row => row.Code).HasColumnName("code").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.CitationId).HasColumnName("citation_id").HasMaxLength(16);
        builder.Property(row => row.Note).HasColumnName("note").HasMaxLength(BriefCheckFlag.MaximumNoteLength).IsRequired();
        builder.HasOne<BriefAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
