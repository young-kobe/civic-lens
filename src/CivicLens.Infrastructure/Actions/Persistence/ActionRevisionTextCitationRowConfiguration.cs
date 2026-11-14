using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionTextCitationRowConfiguration : IEntityTypeConfiguration<ActionRevisionTextCitationRow>
{
    public void Configure(EntityTypeBuilder<ActionRevisionTextCitationRow> builder)
    {
        builder.ToTable("action_revision_text_citations", table =>
        {
            table.HasCheckConstraint("ck_action_revision_text_citations_position", $"position BETWEEN 0 AND {ActionDraftRevision.MaximumTextCitations - 1}");
            table.HasCheckConstraint("ck_action_revision_text_citations_part", EnumChecks.InList("part", Enum.GetNames<ActionTextPart>()));
            table.HasCheckConstraint("ck_action_revision_text_citations_quote", $"citation_length <= {QuotedSentenceLimits.MaximumQuoteLength}");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Part).HasColumnName("part").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Sentence).HasColumnName("sentence").HasMaxLength(QuotedSentenceLimits.MaximumSentenceLength);
        SourceSpanColumns.Configure(builder, "action_revision_text_citations", "Citation", "citation", optional: false);
    }
}
