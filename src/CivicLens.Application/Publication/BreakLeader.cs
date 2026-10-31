using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The official of one chamber and party with the highest share of split votes cast against most of the party, in the newest Congress that
/// any official has in the chamber. Only officials with at least MinimumSplitVotes split votes cast and one break can lead.
/// </summary>
public sealed record BreakLeader(PublishedChamber Chamber, string Party, int Congress, OfficialListing Official, int Cast, int Breaks)
{
    public const int MinimumSplitVotes = 25;

    public static ImmutableArray<BreakLeader> All(IReadOnlyList<OfficialListing> officials, IReadOnlyList<PublishedOfficialSummary> summaries)
    {
        var listings = officials.ToDictionary(official => official.Profile.Id, StringComparer.Ordinal);
        var withVotes = summaries.Where(summary => summary.Congress is not null).ToList();
        return [.. Enum.GetValues<PublishedChamber>().SelectMany(chamber => InChamber(chamber, withVotes, listings))];
    }

    private static IEnumerable<BreakLeader> InChamber(PublishedChamber chamber, List<PublishedOfficialSummary> withVotes,
        Dictionary<string, OfficialListing> listings)
    {
        var inChamber = withVotes.Where(summary => summary.PartyVotes.Any(row => row.Chamber == chamber)).ToList();
        if (inChamber.Count == 0) yield break;
        var congress = inChamber.Max(summary => summary.Congress!.Value);
        foreach (var party in new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican })
        {
            var leader = inChamber.Where(summary => summary.Congress == congress)
                .Select(summary => (summary.Id, Row: summary.PartyVotes.SingleOrDefault(row => row.Chamber == chamber && row.Party == party)))
                .Where(pair => pair.Row is { Breaks: > 0 } && pair.Row.SplitVotesCast >= MinimumSplitVotes)
                .OrderByDescending(pair => pair.Row!.Breaks / (double)pair.Row.SplitVotesCast).ThenByDescending(pair => pair.Row!.Breaks)
                .ThenBy(pair => pair.Id, StringComparer.Ordinal)
                .Select(pair => new BreakLeader(chamber, party, congress, listings[pair.Id], pair.Row!.SplitVotesCast, pair.Row.Breaks))
                .FirstOrDefault();
            if (leader is not null) yield return leader;
        }
    }
}
