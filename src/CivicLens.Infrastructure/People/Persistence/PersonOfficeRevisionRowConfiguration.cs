using CivicLens.Core.Review;
using CivicLens.Application.People;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonOfficeRevisionRowConfiguration : IEntityTypeConfiguration<PersonOfficeRevisionRow>
{
    public void Configure(EntityTypeBuilder<PersonOfficeRevisionRow> builder)
    {
        builder.ToTable("person_office_revisions", table =>
        {
            table.HasCheckConstraint("ck_person_office_revisions_shape",
                "revision_number >= 1 AND length(btrim(note)) > 0 AND length(btrim(actor_subject)) > 0");
        });
        builder.HasKey(row => new { row.PersonId, row.RevisionNumber });
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(PublicationProtocol.MaximumFacetIdLength);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Note).HasColumnName("note").HasMaxLength(PersonRegistryLimits.Note).IsRequired();
        builder.Property(row => row.ActorSubject).HasColumnName("actor_subject").HasMaxLength(ReviewAuthor.MaximumSubjectLength).IsRequired();
        builder.Property(row => row.RecordedAtUtcTicks).HasColumnName("recorded_at_utc_ticks");
        builder.Property(row => row.RegistrySequence).TakesNext();
        builder.HasIndex(row => row.RegistrySequence).IsUnique();
        builder.HasOne<PersonRow>().WithMany().HasForeignKey(row => row.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Terms).WithOne().HasForeignKey(term => new { term.PersonId, term.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
