using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Core.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionFactRowConfiguration : IEntityTypeConfiguration<VoteRevisionFactRow>
{
    public void Configure(EntityTypeBuilder<VoteRevisionFactRow> builder)
    {
        builder.ToTable("vote_revision_facts", table =>
        {
            table.HasCheckConstraint("ck_vote_revision_facts_position", "position >= 0");
            table.HasCheckConstraint("ck_vote_revision_facts_text", $"length(fact_text) BETWEEN 1 AND {VoteDraftRevision.MaximumFactLength}");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Position });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.FactText).HasColumnName("fact_text").IsRequired();
        SourceSpanColumns.Configure(builder, "vote_revision_facts", "Citation", "citation", optional: false);
    }
}
