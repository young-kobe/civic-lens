using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionSponsorshipCitationRowConfiguration : IEntityTypeConfiguration<ActionRevisionSponsorshipCitationRow>
{
    public void Configure(EntityTypeBuilder<ActionRevisionSponsorshipCitationRow> builder)
    {
        builder.ToTable("action_revision_sponsorship_citations", table =>
            table.HasCheckConstraint("ck_action_revision_sponsorship_citations_position",
                $"position BETWEEN 0 AND {ActionDraftRevision.MaximumCitationsPerSponsorship - 1}"));
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.SponsorshipPosition, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.SponsorshipPosition).HasColumnName("sponsorship_position");
        builder.Property(row => row.Position).HasColumnName("position");
        SourceSpanColumns.Configure(builder, "action_revision_sponsorship_citations", "Citation", "citation", optional: false);
    }
}
