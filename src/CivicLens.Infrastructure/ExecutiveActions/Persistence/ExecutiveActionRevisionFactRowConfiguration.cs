using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionFactRowConfiguration : IEntityTypeConfiguration<ExecutiveActionRevisionFactRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionRevisionFactRow> builder)
    {
        builder.ToTable("executive_action_revision_facts", table =>
            table.HasCheckConstraint("ck_executive_action_revision_facts_position", "position >= 0"));
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        SourceSpanColumns.Configure(builder, "executive_action_revision_facts", "Citation", "citation", optional: false);
    }
}
