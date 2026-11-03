using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionNoticeRowConfiguration : IEntityTypeConfiguration<ExecutiveActionRevisionNoticeRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionRevisionNoticeRow> builder)
    {
        builder.ToTable("executive_action_revision_notices", table =>
            table.HasCheckConstraint("ck_executive_action_revision_notices_position", "position >= 0"));
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.DocumentNumber).HasColumnName("document_number").HasMaxLength(32);
        builder.HasIndex(row => new { row.RecordId, row.RevisionNumber, row.DocumentNumber }).IsUnique();
        builder.HasOne<ExecutiveActionDocumentRow>().WithMany()
            .HasForeignKey(row => new { row.SourceId, row.DocumentNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
