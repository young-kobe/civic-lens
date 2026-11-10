namespace CivicLens.Publication.Contracts;

/// <summary>The casts of every member in one resolved party group on one roll call.</summary>
public sealed record PublishedPartyTally
{
    public required string Party { get; init; }
    public required int Yea { get; init; }
    public required int Nay { get; init; }
    public required int Present { get; init; }
    public required int NotVoting { get; init; }
    public required int Other { get; init; }

    public void Validate()
    {
        var memberCount = (long)Yea + Nay + Present + NotVoting + Other;
        if (!PublishedEvidenceRules.IsBoundedText(Party) || Yea < 0 || Nay < 0 || Present < 0 || NotVoting < 0 || Other < 0 ||
            memberCount is 0 or > PublishedVote.MaximumMembers)
            throw new InvalidDataException("Published party tally is invalid.");
    }
}
