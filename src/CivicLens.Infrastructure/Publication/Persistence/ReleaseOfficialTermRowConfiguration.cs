using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialTermRowConfiguration : IEntityTypeConfiguration<ReleaseOfficialTermRow>
{
    public void Configure(EntityTypeBuilder<ReleaseOfficialTermRow> builder)
    {
        builder.ToTable("release_official_terms", table =>
        {
            table.HasCheckConstraint("ck_release_official_terms_position", "position >= 0");
            table.HasCheckConstraint("ck_release_official_terms_office", "length(btrim(office)) > 0");
            table.HasCheckConstraint("ck_release_official_terms_district", "district IS NULL OR district >= 0");
            table.HasCheckConstraint("ck_release_official_terms_dates", "end_on IS NULL OR end_on >= start_on");
            table.HasCheckConstraint("ck_release_official_terms_kind", EnumChecks.InList("kind", Enum.GetNames<PublishedOfficeKind>()));
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.OfficialId, row.Position });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Office).HasColumnName("office").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32);
        builder.Property(row => row.State).HasColumnName("state").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.District).HasColumnName("district");
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Caucus).HasColumnName("caucus").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.StartOn).HasColumnName("start_on");
        builder.Property(row => row.EndOn).HasColumnName("end_on");
        builder.HasOne<ReleaseOfficialRow>().WithMany().HasForeignKey(row => new { row.ReleaseNumber, row.OfficialId }).OnDelete(DeleteBehavior.Restrict);
    }
}
