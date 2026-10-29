using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionRevisionRowConfiguration : IEntityTypeConfiguration<ExecutiveActionRevisionRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionRevisionRow> builder)
    {
        builder.ToTable("executive_action_revisions", table =>
            table.HasCheckConstraint("ck_executive_action_revisions_number", "revision_number >= 1"));
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasOne<ExecutiveActionDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
    }
}
