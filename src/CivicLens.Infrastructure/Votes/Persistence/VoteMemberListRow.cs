namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteMemberListRow
{
    public string ListId { get; set; } = string.Empty;
    public short MemberCount { get; set; }
    public long CreatedAtUtcTicks { get; set; }
}
