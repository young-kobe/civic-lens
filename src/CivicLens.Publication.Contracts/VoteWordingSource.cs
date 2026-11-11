namespace CivicLens.Publication.Contracts;

/// <summary>The cited parts of a vote that the approved wording reads. A published vote and a vote detail answer both supply them.</summary>
public sealed record VoteWordingSource(PublishedVoteKind Kind, PublishedChamber Chamber, PublishedVoteSource Source, string? Result,
    PublishedCitation? KindCitation, PublishedVoteFact[]? Facts, string? BillType, int? BillNumber)
{
    /// <summary>True when a cited title says that the measure proposes an amendment to the Constitution. Such a measure needs more than a simple majority, so no wording or figure may treat the vote as an ordinary law vote.</summary>
    public bool PossibleConstitutionalPurpose => Facts?.Any(fact =>
        fact.Text.StartsWith(PublishedVote.TitlePrefix + "Proposing an amendment to the Constitution", StringComparison.OrdinalIgnoreCase)) == true;

    public static VoteWordingSource Of(PublishedVote vote) =>
        new(vote.Kind, vote.Chamber, vote.Source, vote.Result, vote.KindCitation, vote.Facts, vote.BillType, vote.BillNumber);
}
