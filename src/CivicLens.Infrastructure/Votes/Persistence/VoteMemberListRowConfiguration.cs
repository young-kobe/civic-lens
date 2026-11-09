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
            table.HasCheckConstraint("ck_vote_member_lists_totals", "yea_count >= 0 AND nay_count >= 0 AND present_count >= 0 AND not_voting_count >= 0 AND other_count >= 0 AND (yea_count::integer + nay_count + present_count + not_voting_count + other_count) = member_count");
        });
        builder.HasKey(row => row.ListId);
        builder.Property(row => row.ListId).HasColumnName("list_id").HasMaxLength(64);
        builder.Property(row => row.MemberCount).HasColumnName("member_count");
        builder.Property(row => row.YeaCount).HasColumnName("yea_count");
        builder.Property(row => row.NayCount).HasColumnName("nay_count");
        builder.Property(row => row.PresentCount).HasColumnName("present_count");
        builder.Property(row => row.NotVotingCount).HasColumnName("not_voting_count");
        builder.Property(row => row.OtherCount).HasColumnName("other_count");
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
    }
}
