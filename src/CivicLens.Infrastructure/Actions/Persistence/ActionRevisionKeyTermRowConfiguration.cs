using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionKeyTermRowConfiguration : IEntityTypeConfiguration<ActionRevisionKeyTermRow>
{
    public void Configure(EntityTypeBuilder<ActionRevisionKeyTermRow> builder)
    {
        builder.ToTable("action_revision_key_terms", table =>
        {
            table.HasCheckConstraint("ck_action_revision_key_terms_position", $"position BETWEEN 0 AND {ActionKeyTerm.MaximumPerRevision - 1}");
            table.HasCheckConstraint("ck_action_revision_key_terms_term", "length(btrim(term)) > 0");
            table.HasCheckConstraint("ck_action_revision_key_terms_quote",
                $"citation_length <= {ActionDraftRevision.MaximumTextQuoteLength} AND length(btrim(definition)) > 0");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Term).HasColumnName("term").HasMaxLength(ActionKeyTerm.MaximumTermLength);
        builder.Property(row => row.Definition).HasColumnName("definition").HasMaxLength(ActionDraftRevision.MaximumTextQuoteLength);
        builder.Property(row => row.Cut).HasColumnName("cut");
        // The unique index ux_action_revision_key_terms_term on (record_id, revision_number, lower(term)) is SQL in the migration, because EF Core maps
        // no expression index. It refuses a repeated term with case ignored, as the revision check does.
        SourceSpanColumns.Configure(builder, "action_revision_key_terms", "Citation", "citation", optional: false);
    }
}
