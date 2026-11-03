using CivicLens.Core.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionSponsorshipRowConfiguration : IEntityTypeConfiguration<ActionRevisionSponsorshipRow>
{
    public void Configure(EntityTypeBuilder<ActionRevisionSponsorshipRow> builder)
    {
        builder.ToTable("action_revision_sponsorships", table =>
        {
            table.HasCheckConstraint("ck_action_revision_sponsorships_position", "position >= 0");
            table.HasCheckConstraint("ck_action_revision_sponsorships_person", "length(btrim(person_id)) > 0 AND length(btrim(bioguide_id)) > 0");
            table.HasCheckConstraint("ck_action_revision_sponsorships_role", EnumChecks.InList("role", Enum.GetNames<ActionSponsorshipRole>()));
            table.HasCheckConstraint("ck_action_revision_sponsorships_withdrawn", "withdrawn_on >= sponsored_on");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(ActionDraftRevision.MaximumPersonIdLength).IsRequired();
        builder.Property(row => row.BioguideId).HasColumnName("bioguide_id").HasMaxLength(ActionDraftRevision.MaximumBioguideIdLength).IsRequired();
        builder.Property(row => row.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.IsOriginal).HasColumnName("is_original");
        builder.Property(row => row.SponsoredOn).HasColumnName("sponsored_on");
        builder.Property(row => row.WithdrawnOn).HasColumnName("withdrawn_on");
        builder.HasIndex(row => new { row.PersonId, row.RecordId, row.RevisionNumber }).HasDatabaseName("ix_action_revision_sponsorships_person");
        builder.HasMany(row => row.Citations).WithOne()
            .HasForeignKey(citation => new { citation.RecordId, citation.RevisionNumber, citation.SponsorshipPosition }).OnDelete(DeleteBehavior.Restrict);
    }
}
