namespace CivicLens.Application.Collection.VoteviewVotes;

public sealed record VoteviewCast(int Part, int Icpsr, string Cast)
{
    public const string Yea = "Yea";
    public const string Nay = "Nay";
    public const string Present = "Present";
    public const string NotVoting = "Not Voting";
}
