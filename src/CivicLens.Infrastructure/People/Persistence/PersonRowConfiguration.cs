using CivicLens.Application.People;
using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonRowConfiguration : IEntityTypeConfiguration<PersonRow>
{
    public void Configure(EntityTypeBuilder<PersonRow> builder)
    {
        builder.ToTable("people", table =>
        {
            table.HasCheckConstraint("ck_people_identity",
                "person_id ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND (bioguide_id IS NULL OR bioguide_id ~ '^[A-Z][0-9]{6}$') " +
                "AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND (govtrack_id IS NULL OR govtrack_id > 0)");
            table.HasCheckConstraint("ck_people_creator", "num_nonnulls(created_by_attempt_id, created_by_actor) = 1");
        });
        builder.HasKey(row => row.PersonId);
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(PublicationProtocol.MaximumFacetIdLength);
        builder.Property(row => row.BioguideId).HasColumnName("bioguide_id").HasMaxLength(7);
        builder.Property(row => row.WikidataId).HasColumnName("wikidata_id").HasMaxLength(RosterLimits.WikidataId);
        builder.Property(row => row.GovtrackId).HasColumnName("govtrack_id");
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.CreatedByAttemptId).HasColumnName("created_by_attempt_id");
        builder.Property(row => row.CreatedByActor).HasColumnName("created_by_actor").HasMaxLength(PersonRegistryLimits.ActorSubject);
        builder.HasIndex(row => row.BioguideId).IsUnique();
        builder.HasIndex(row => row.WikidataId).IsUnique();
        builder.HasIndex(row => row.GovtrackId).IsUnique();
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.CreatedByAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
