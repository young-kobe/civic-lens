using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionPartyTallyRowConfiguration : IEntityTypeConfiguration<VoteRevisionPartyTallyRow>
{
    public void Configure(EntityTypeBuilder<VoteRevisionPartyTallyRow> builder)
    {
        builder.ToTable("vote_revision_party_tallies", table =>
        {
            table.HasCheckConstraint("ck_vote_revision_party_tallies_party", $"length(btrim(party)) > 0 AND length(party) <= {PublishedRecordEntry.MaximumFilterValueLength}");
            table.HasCheckConstraint("ck_vote_revision_party_tallies_buckets", "yea >= 0 AND nay >= 0 AND present >= 0 AND not_voting >= 0 AND other >= 0");
            table.HasCheckConstraint("ck_vote_revision_party_tallies_total", $"yea + nay + present + not_voting + other BETWEEN 1 AND {PublishedVote.MaximumMembers}");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Party }).HasName("PK_vote_revision_party_tallies");
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Yea).HasColumnName("yea");
        builder.Property(row => row.Nay).HasColumnName("nay");
        builder.Property(row => row.Present).HasColumnName("present");
        builder.Property(row => row.NotVoting).HasColumnName("not_voting");
        builder.Property(row => row.Other).HasColumnName("other");
    }
}
