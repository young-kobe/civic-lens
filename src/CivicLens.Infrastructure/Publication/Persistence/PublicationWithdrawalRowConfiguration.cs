using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationWithdrawalRowConfiguration : IEntityTypeConfiguration<PublicationWithdrawalRow>
{
    public void Configure(EntityTypeBuilder<PublicationWithdrawalRow> builder)
    {
        builder.ToTable("publication_withdrawals", table =>
        {
            table.HasCheckConstraint("ck_publication_withdrawals_range", "last_release IS NULL OR last_release >= first_release");
            table.HasCheckConstraint("ck_publication_withdrawals_revision", "withdrawn_revision_number >= 1");
            table.HasCheckConstraint("ck_publication_withdrawals_note", $"char_length(note) BETWEEN 1 AND {PublishedCorrection.MaximumNoteLength}");
        });
        builder.HasKey(row => new { row.Kind, row.RecordId, row.FirstRelease });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.WithdrawnRevisionNumber).HasColumnName("withdrawn_revision_number").IsRequired();
        builder.Property(row => row.WithdrawnAtUtcTicks).HasColumnName("withdrawn_at_utc_ticks").IsRequired();
        builder.Property(row => row.Note).HasColumnName("note").IsRequired();
        builder.Property(row => row.FirstRelease).HasColumnName("first_release").IsRequired();
        builder.Property(row => row.LastRelease).HasColumnName("last_release");
        builder.HasOne<PublishedRecordRow>().WithMany().HasForeignKey(row => new { row.Kind, row.RecordId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.FirstRelease).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.LastRelease).OnDelete(DeleteBehavior.Restrict);
    }
}
