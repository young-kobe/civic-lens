using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationReleaseRowConfiguration : IEntityTypeConfiguration<PublicationReleaseRow>
{
    public void Configure(EntityTypeBuilder<PublicationReleaseRow> builder)
    {
        builder.ToTable("publication_releases", table =>
        {
            table.HasCheckConstraint("ck_publication_releases_number", "release_number BETWEEN 1 AND 999999");
            table.HasCheckConstraint("ck_publication_releases_directory", "directory_name ~ '^[0-9]{6}-[0-9a-f]{32}$'");
            table.HasCheckConstraint("ck_publication_releases_published", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_publication_releases_count", "record_count BETWEEN 0 AND 2000");
        });
        builder.HasKey(row => row.ReleaseNumber);
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number").ValueGeneratedNever();
        builder.Property(row => row.DirectoryName).HasColumnName("directory_name").HasMaxLength(64).IsRequired();
        builder.Property(row => row.ActorSubject).HasColumnName("actor_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.PublishedAtUtcTicks).HasColumnName("published_at_utc_ticks").IsRequired();
        builder.Property(row => row.RecordCount).HasColumnName("record_count").IsRequired();
        builder.Property(row => row.RecordsJson).HasColumnName("records_json").IsRequired();
        builder.Property(row => row.IsActive).HasColumnName("is_active").IsRequired();
        builder.HasIndex(row => row.DirectoryName).IsUnique();
        builder.HasIndex(row => row.IsActive).IsUnique().HasFilter("is_active");
    }
}
