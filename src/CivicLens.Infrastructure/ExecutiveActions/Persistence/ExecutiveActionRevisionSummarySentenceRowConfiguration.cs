using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionSummarySentenceRowConfiguration : IEntityTypeConfiguration<ExecutiveActionRevisionSummarySentenceRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionRevisionSummarySentenceRow> builder)
    {
        builder.ToTable("executive_action_revision_summary_sentences", table =>
        {
            table.HasCheckConstraint("ck_executive_action_revision_summary_sentences_position", $"position BETWEEN 0 AND {QuotedSentenceLimits.MaximumSentences - 1}");
            table.HasCheckConstraint("ck_executive_action_revision_summary_sentences_quote", $"quote_length <= {QuotedSentenceLimits.MaximumQuoteLength}");
            table.HasCheckConstraint("ck_executive_action_revision_summary_sentences_sentence", "btrim(sentence) <> ''");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Sentence).HasColumnName("sentence").HasMaxLength(QuotedSentenceLimits.MaximumSentenceLength);
        SourceSpanColumns.Configure(builder, "executive_action_revision_summary_sentences", "Quote", "quote", optional: false);
    }
}
