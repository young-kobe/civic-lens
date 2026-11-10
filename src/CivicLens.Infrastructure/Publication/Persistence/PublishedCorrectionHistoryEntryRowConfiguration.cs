using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedCorrectionHistoryEntryRowConfiguration : IEntityTypeConfiguration<PublishedCorrectionHistoryEntryRow>
{
    private const long MaximumDateTimeTicks = 3155378975999999999;

    public void Configure(EntityTypeBuilder<PublishedCorrectionHistoryEntryRow> builder)
    {
        builder.ToTable("published_correction_history_entries", table =>
        {
            table.HasCheckConstraint("ck_published_correction_history_entries_position", "position BETWEEN 0 AND 61");
            table.HasCheckConstraint("ck_published_correction_history_entries_replaced_revision_number", "replaced_revision_number >= 1");
            table.HasCheckConstraint("ck_published_correction_history_entries_published_offset_minutes",
                "published_offset_minutes BETWEEN -840 AND 840");
            table.HasCheckConstraint("ck_published_correction_history_entries_local_ticks",
                $"published_at_utc_ticks + published_offset_minutes::bigint * 600000000 BETWEEN 0 AND {MaximumDateTimeTicks}");
            table.HasCheckConstraint("ck_published_correction_history_entries_note", "note IS NULL OR length(note) <= 500");
            table.HasCheckConstraint("ck_published_correction_history_entries_note_facts_unchanged",
                "note IS NULL OR NOT facts_unchanged");
        });
        builder.HasKey(row => new { row.HistoryId, row.Position });
        builder.Property(row => row.HistoryId).HasColumnName("history_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.Position).HasColumnName("position").IsRequired();
        builder.Property(row => row.ReplacedRevisionNumber).HasColumnName("replaced_revision_number").IsRequired();
        builder.Property(row => row.PublishedAtUtcTicks).HasColumnName("published_at_utc_ticks").IsRequired();
        builder.Property(row => row.PublishedOffsetMinutes).HasColumnName("published_offset_minutes").IsRequired();
        builder.Property(row => row.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(row => row.FactsUnchanged).HasColumnName("facts_unchanged").IsRequired();
        builder.HasOne<PublishedCorrectionHistoryRow>().WithMany().HasForeignKey(row => row.HistoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
