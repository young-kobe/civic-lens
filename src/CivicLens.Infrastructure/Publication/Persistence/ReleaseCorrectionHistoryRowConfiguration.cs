using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseCorrectionHistoryRowConfiguration : IEntityTypeConfiguration<ReleaseCorrectionHistoryRow>
{
    public void Configure(EntityTypeBuilder<ReleaseCorrectionHistoryRow> builder)
    {
        builder.ToTable("release_correction_histories", table =>
        {
            table.HasCheckConstraint("ck_release_correction_histories_kind",
                EnumChecks.InList("kind", Enum.GetValues<PublishedRecordKind>().Select(PublishedRecordReference.NameOf)));
            table.HasCheckConstraint("ck_release_correction_histories_record_id",
                "record_id ~ CASE WHEN kind = 'documentChange' THEN '^[0-9a-f]{32}$' ELSE '^[0-9a-f]{64}$' END");
            table.HasCheckConstraint("ck_release_correction_histories_revision_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_release_correction_histories_history_id", "history_id ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.Kind, row.RecordId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number").IsRequired();
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(row => row.HistoryId).HasColumnName("history_id").HasMaxLength(64).IsRequired();
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedCorrectionHistoryRow>().WithMany().HasForeignKey(row => row.HistoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublishedEntryRow>().WithMany()
            .HasForeignKey(row => new { row.Kind, row.RecordId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
