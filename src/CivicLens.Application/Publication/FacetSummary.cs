using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Counts of the votes and bills among the manifest entries of one issue or policy area. The counts cover every page of the facet.</summary>
public sealed record FacetSummary(int Votes, int SplitVotes, int PartiesAgreedVotes, int Bills)
{
    public static FacetSummary From(IEnumerable<PublishedRecordEntry> records)
    {
        var entries = records.ToList();
        var votes = entries.Where(entry => entry.Kind == PublishedRecordKind.Vote).ToList();
        return new(votes.Count,
            votes.Count(entry => entry.Alignment == PublishedVoteAlignment.Split),
            votes.Count(entry => entry.Alignment == PublishedVoteAlignment.PartiesAgree),
            entries.Count(entry => entry.Kind == PublishedRecordKind.Action));
    }
}
