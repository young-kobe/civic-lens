namespace CivicLens.Publication.Contracts;

/// <summary>The split votes of one official in one chamber and party: casts of Yea or Nay, and how many went against the party majority.</summary>
public sealed record PublishedPartyVoteSummary
{
    public required PublishedChamber Chamber { get; init; }
    public required string Party { get; init; }
    public required int SplitVotesCast { get; init; }
    public required int Breaks { get; init; }

    public void Validate()
    {
        if (!Enum.IsDefined(Chamber) || !PublishedPartyMajority.IsMajorParty(Party) || SplitVotesCast < 0 ||
            SplitVotesCast > PublishedOfficialVotes.MaximumVotes || Breaks < 0 || Breaks > SplitVotesCast)
            throw new InvalidDataException("Published party vote summary is invalid.");
    }
}
