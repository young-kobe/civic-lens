using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationWithdrawalCorrectionRowConfiguration : IEntityTypeConfiguration<PublicationWithdrawalCorrectionRow>
{
    public void Configure(EntityTypeBuilder<PublicationWithdrawalCorrectionRow> builder)
    {
        builder.ToTable("publication_withdrawal_corrections", table =>
        {
            table.HasCheckConstraint("ck_publication_withdrawal_corrections_position", "\"position\" >= 0");
            table.HasCheckConstraint("ck_publication_withdrawal_corrections_revision", "replaced_revision_number >= 1");
            table.HasCheckConstraint("ck_publication_withdrawal_corrections_note",
                $"note IS NULL OR char_length(note) BETWEEN 1 AND {PublishedCorrection.MaximumNoteLength} AND btrim(note) <> ''");
            table.HasCheckConstraint("ck_publication_withdrawal_corrections_text_rewrite",
                EnumChecks.InList("text_rewrite", Enum.GetNames<PublishedTextRewrite>()));
            table.HasCheckConstraint("ck_publication_withdrawal_corrections_note_text_rewrite",
                $"note IS NULL OR text_rewrite = '{nameof(PublishedTextRewrite.None)}'");
        });
        builder.HasKey(row => new { row.Kind, row.RecordId, row.FirstRelease, row.Position });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.FirstRelease).HasColumnName("first_release").IsRequired();
        builder.Property(row => row.Position).HasColumnName("position").IsRequired();
        builder.Property(row => row.ReplacedRevisionNumber).HasColumnName("replaced_revision_number").IsRequired();
        builder.Property(row => row.PublishedAtUtcTicks).HasColumnName("published_at_utc_ticks").IsRequired();
        builder.Property(row => row.Note).HasColumnName("note");
        builder.Property(row => row.TextRewrite).HasColumnName("text_rewrite").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasOne<PublicationWithdrawalRow>().WithMany().HasForeignKey(row => new { row.Kind, row.RecordId, row.FirstRelease })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
