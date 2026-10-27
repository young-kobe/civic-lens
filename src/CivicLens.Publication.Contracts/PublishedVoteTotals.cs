namespace CivicLens.Publication.Contracts;

/// <summary>The counts of one whole roll call, from the source. Each count covers every member of the chamber who has that cast.</summary>
public sealed record PublishedVoteTotals
{
    public required int Yea { get; init; }
    public required int Nay { get; init; }
    public required int Present { get; init; }
    public required int NotVoting { get; init; }

    public void Validate()
    {
        if (Yea < 0 || Nay < 0 || Present < 0 || NotVoting < 0 || (long)Yea + Nay + Present + NotVoting > PublishedVote.MaximumMembers)
            throw new InvalidDataException("Published vote totals are invalid.");
    }
}
