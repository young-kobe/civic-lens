namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteMemberListRow
{
    public string ListId { get; set; } = string.Empty;
    public short MemberCount { get; set; }
    public short YeaCount { get; set; }
    public short NayCount { get; set; }
    public short PresentCount { get; set; }
    public short NotVotingCount { get; set; }
    public short OtherCount { get; set; }
    public long CreatedAtUtcTicks { get; set; }
}
