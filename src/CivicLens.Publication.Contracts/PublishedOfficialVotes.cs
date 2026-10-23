namespace CivicLens.Publication.Contracts;

/// <summary>The votes of one official in one Congress, newest first.</summary>
public sealed record PublishedOfficialVotes
{
    public const int MaximumVotes = 5000;
    public const int MaximumBytesPerVote = 52 * 1024;
    public const int MaximumBytes = MaximumVotes * MaximumBytesPerVote;

    public required PublishedOfficialVote[] Votes { get; init; }

    public static PublishedOfficialVotes Of(IEnumerable<PublishedOfficialVote> votes)
    {
        var file = new PublishedOfficialVotes
        {
            Votes = [.. votes.OrderByDescending(vote => vote.VotedOn).ThenBy(vote => vote.Chamber).ThenByDescending(vote => vote.Session)
                .ThenByDescending(vote => vote.RollCallNumber).ThenBy(vote => vote.RecordId, StringComparer.Ordinal)]
        };
        file.Validate();
        return file;
    }

    public void Validate()
    {
        if (Votes is null || Votes.Length > MaximumVotes || Votes.Any(vote => vote is null))
            throw new InvalidDataException($"Published official votes allow at most {MaximumVotes} votes.");
        foreach (var vote in Votes) vote.Validate();
        if (Votes.Select(vote => vote.Congress).Distinct().Count() > 1 ||
            !PublishedEvidenceRules.AreDistinctAndNotBlank(Votes.Select(vote => vote.RecordId)))
            throw new InvalidDataException("Published official votes must be distinct and from one Congress.");
    }
}
