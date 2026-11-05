using CivicLens.Application.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.People.Persistence;

internal sealed class PersonOfficeRevisionTermRowConfiguration : IEntityTypeConfiguration<PersonOfficeRevisionTermRow>
{
    public void Configure(EntityTypeBuilder<PersonOfficeRevisionTermRow> builder)
    {
        builder.ToTable("person_office_revision_terms", table =>
        {
            table.HasCheckConstraint("ck_person_office_revision_terms_kind", "kind IN ('Executive', 'Judicial')");
            table.HasCheckConstraint("ck_person_office_revision_terms_shape",
                "position >= 0 AND length(btrim(title)) > 0 AND (party IS NULL OR length(btrim(party)) > 0) AND (ends_on IS NULL OR ends_on >= starts_on)");
        });
        builder.HasKey(row => new { row.PersonId, row.RevisionNumber, row.Position });
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(PublicationProtocol.MaximumFacetIdLength);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Title).HasColumnName("title").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength).IsRequired();
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.StartsOn).HasColumnName("starts_on");
        builder.Property(row => row.EndsOn).HasColumnName("ends_on");
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
    }
}
