using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedEntryOfficialRowConfiguration : IEntityTypeConfiguration<PublishedEntryOfficialRow>
{
    public void Configure(EntityTypeBuilder<PublishedEntryOfficialRow> builder)
    {
        builder.ToTable("published_entry_officials", table =>
        {
            table.HasCheckConstraint("ck_published_entry_officials_position", "position >= 0");
            table.HasCheckConstraint("ck_published_entry_officials_matched_kind", $"NOT is_matched OR kind = '{PublishedRecordReference.NameOf(PublishedRecordKind.Relationship)}'");
            table.HasCheckConstraint("ck_published_entry_officials_official_id", "length(btrim(official_id)) > 0");
            table.HasCheckConstraint("ck_published_entry_officials_official_name", "length(btrim(official_name)) > 0");
        });
        builder.HasKey(row => new { row.Kind, row.RecordId, row.RevisionNumber, row.Position });
        builder.HasAlternateKey(row => new { row.Kind, row.RecordId, row.RevisionNumber, row.OfficialId });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.OfficialName).HasColumnName("official_name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.IsMatched).HasColumnName("is_matched").HasDefaultValue(false);
        builder.HasIndex(row => new { row.Kind, row.RecordId, row.RevisionNumber }, "ux_published_entry_officials_matched").IsUnique()
            .HasFilter("is_matched");
        builder.HasOne<PublishedEntryRow>().WithMany().HasForeignKey(row => new { row.Kind, row.RecordId, row.RevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
