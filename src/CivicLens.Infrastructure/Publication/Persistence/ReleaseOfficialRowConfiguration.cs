using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialRowConfiguration : IEntityTypeConfiguration<ReleaseOfficialRow>
{
    public void Configure(EntityTypeBuilder<ReleaseOfficialRow> builder)
    {
        builder.ToTable("release_officials", table =>
        {
            table.HasCheckConstraint("ck_release_officials_official_id", "length(btrim(official_id)) > 0");
            table.HasCheckConstraint("ck_release_officials_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_release_officials_photo_path", "photo_path IS NULL OR length(btrim(photo_path)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.OfficialId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.WikidataId).HasColumnName("wikidata_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Website).HasColumnName("website");
        builder.Property(row => row.PhotoPath).HasColumnName("photo_path").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
