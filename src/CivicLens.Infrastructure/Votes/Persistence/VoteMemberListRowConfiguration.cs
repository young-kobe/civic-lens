using CivicLens.Core.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteMemberListRowConfiguration : IEntityTypeConfiguration<VoteMemberListRow>
{
    public void Configure(EntityTypeBuilder<VoteMemberListRow> builder)
    {
        builder.ToTable("vote_member_lists", table =>
        {
            table.HasCheckConstraint("ck_vote_member_lists_id", "list_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_member_lists_count", $"member_count BETWEEN 1 AND {VoteDraftRevision.MaximumMembers}");
        });
        builder.HasKey(row => row.ListId);
        builder.Property(row => row.ListId).HasColumnName("list_id").HasMaxLength(64);
        builder.Property(row => row.MemberCount).HasColumnName("member_count");
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
    }
}
