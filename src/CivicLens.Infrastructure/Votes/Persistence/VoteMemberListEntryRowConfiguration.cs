using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteMemberListEntryRowConfiguration : IEntityTypeConfiguration<VoteMemberListEntryRow>
{
    public void Configure(EntityTypeBuilder<VoteMemberListEntryRow> builder)
    {
        builder.ToTable("vote_member_list_entries", table =>
        {
            table.HasCheckConstraint("ck_vote_member_list_entries_position", "position >= 0");
            table.HasCheckConstraint("ck_vote_member_list_entries_member",
                "length(btrim(person_id)) > 0 AND length(btrim(bioguide_id)) > 0 AND length(btrim(vote_cast)) > 0");
            table.HasCheckConstraint("ck_vote_member_list_entries_party", "party IS NULL OR length(btrim(party)) > 0");
        });
        builder.HasKey(row => new { row.ListId, row.Position });
        builder.Property(row => row.ListId).HasColumnName("list_id").HasMaxLength(64);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.PersonId).HasColumnName("person_id").HasMaxLength(VoteDraftRevision.MaximumPersonIdLength).IsRequired();
        builder.Property(row => row.BioguideId).HasColumnName("bioguide_id").HasMaxLength(VoteDraftRevision.MaximumBioguideIdLength).IsRequired();
        builder.Property(row => row.VoteCast).HasColumnName("vote_cast").HasMaxLength(VoteDraftRevision.MaximumCastLength).IsRequired();
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        SourceSpanColumns.Configure(builder, "vote_member_list_entries", "Citation", "citation", optional: false);
        builder.HasOne<VoteMemberListRow>().WithMany().HasForeignKey(row => row.ListId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.PersonId, row.ListId }).HasDatabaseName("ix_vote_member_list_entries_person");
        builder.HasIndex(row => row.ListId)
            .HasDatabaseName("ix_vote_member_list_entries_tallies")
            .IncludeProperties(row => row.VoteCast);
    }
}
