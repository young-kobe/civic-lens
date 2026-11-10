namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionPartyTallyRow
{
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string Party { get; set; } = string.Empty;
    public short Yea { get; set; }
    public short Nay { get; set; }
    public short Present { get; set; }
    public short NotVoting { get; set; }
    public short Other { get; set; }
}
