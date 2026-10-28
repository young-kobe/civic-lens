using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Roster.Persistence;

internal sealed class OfficialPhotoRowConfiguration : IEntityTypeConfiguration<OfficialPhotoRow>
{
    public void Configure(EntityTypeBuilder<OfficialPhotoRow> builder)
    {
        builder.ToTable("official_photos");
        builder.HasKey(row => row.AttemptId);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(PublicationProtocol.MaximumFacetIdLength).IsRequired();
        builder.Property(row => row.SourceUrl).HasColumnName("source_url").HasMaxLength(RosterLimits.Url).IsRequired();
        builder.Property(row => row.Credit).HasColumnName("credit").HasMaxLength(RosterLimits.PhotoCredit).IsRequired();
        builder.HasIndex(row => row.PersonId);
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
