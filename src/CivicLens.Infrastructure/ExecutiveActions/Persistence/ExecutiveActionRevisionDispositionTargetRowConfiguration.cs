using CivicLens.Core.ExecutiveActions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionDispositionTargetRowConfiguration : IEntityTypeConfiguration<ExecutiveActionRevisionDispositionTargetRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionRevisionDispositionTargetRow> builder)
    {
        builder.ToTable("executive_action_revision_disposition_targets", table =>
        {
            table.HasCheckConstraint("ck_executive_action_revision_disposition_targets_position", "position >= 0");
            table.HasCheckConstraint("ck_executive_action_revision_disposition_targets_relation",
                EnumChecks.InList("relation", Enum.GetNames<ExecutiveActionRelation>()));
            table.HasCheckConstraint("ck_executive_action_revision_disposition_targets_kind",
                EnumChecks.InList("kind", [nameof(ExecutiveActionKind.ExecutiveOrder), nameof(ExecutiveActionKind.Proclamation)]));
            table.HasCheckConstraint("ck_executive_action_revision_disposition_targets_number", "number ~ '^[1-9][0-9]{0,5}$'");
            table.HasCheckConstraint("ck_executive_action_revision_disposition_targets_span", "reference_start >= 0 AND reference_length > 0");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Relation).HasColumnName("relation").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.Number).HasColumnName("number").HasMaxLength(6);
        builder.Property(row => row.ReferenceStart).HasColumnName("reference_start");
        builder.Property(row => row.ReferenceLength).HasColumnName("reference_length");
        builder.HasIndex(row => new { row.RecordId, row.RevisionNumber, row.Relation, row.Kind, row.Number }).IsUnique();
    }
}
