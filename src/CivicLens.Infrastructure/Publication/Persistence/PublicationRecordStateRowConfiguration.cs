using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublicationRecordStateRowConfiguration : IEntityTypeConfiguration<PublicationRecordStateRow>
{
    public void Configure(EntityTypeBuilder<PublicationRecordStateRow> builder)
    {
        builder.ToTable("publication_record_states", table =>
        {
            table.HasCheckConstraint("ck_publication_record_states_range", "last_release IS NULL OR last_release >= first_release");
        });
        builder.HasKey(row => new { row.Kind, row.RecordId, row.FirstRelease });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(row => row.FirstRelease).HasColumnName("first_release").IsRequired();
        builder.Property(row => row.LastRelease).HasColumnName("last_release");
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.FirstRelease).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.LastRelease).OnDelete(DeleteBehavior.Restrict);
    }
}
