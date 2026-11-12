using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The totals of one roll call and the majority of each party. The House totals are the sum of its party total lines. The Senate totals are its count line.
/// The member lines must agree with those lines, so a wrong total or a wrong member line never publishes.
/// A vote with candidate total lines, such as the election of the Speaker, has no totals and no party majorities, because its casts are candidate names.
/// </summary>
internal sealed class VoteTally
{
    private readonly Dictionary<string, string> partyBySourceId;

    private VoteTally(PublishedVoteTotals? totals, PublishedPartyMajority[] partyMajorities, PublishedPartyTally[]? partyTallies,
        Dictionary<string, string> partyBySourceId)
    {
        Totals = totals;
        PartyMajorities = partyMajorities;
        PartyTallies = partyTallies;
        this.partyBySourceId = partyBySourceId;
    }

    public PublishedVoteTotals? Totals { get; }

    public PublishedPartyMajority[] PartyMajorities { get; }

    public PublishedPartyTally[]? PartyTallies { get; }

    public string PartyOf(string sourceId) => partyBySourceId[sourceId];

    public static VoteTally Read(string recordId, Chamber chamber, ImmutableArray<VoteFact> facts, string membersText, DateOnly day,
        VoteParties parties)
    {
        var lines = membersText.Split('\n').Where(line => line.StartsWith(VoteMemberLine.Prefix, StringComparison.Ordinal))
            .Select(VoteMemberLine.Parse).ToArray();
        var partyBySourceId = lines.ToDictionary(line => line.SourceId, line => parties.PartyOn(line, chamber, day), StringComparer.Ordinal);
        if (PublishedVote.IsCandidateVote(facts.Select(fact => fact.Text)))
        {
            var candidateTallies = HasCompleteCandidateCasts(facts, lines) ? Tallies(lines, partyBySourceId) : null;
            if (candidateTallies?.Any(tally => tally.Yea != 0 || tally.Nay != 0) == true) candidateTallies = null;
            return new(null, [], candidateTallies, partyBySourceId);
        }

        var partyTotals = chamber == Chamber.Senate ? SenateTotals(recordId, facts) : HouseTotals(recordId, facts);
        var totals = Sum(partyTotals.Values);
        if (TotalsOf(lines) != totals)
            throw new ArgumentException($"Vote {recordId} has member lines that do not add up to its total lines.");
        if (chamber == Chamber.House) RequireLettersMatch(recordId, lines, partyTotals);

        var tallies = Tallies(lines, partyBySourceId);
        var majorities = tallies.Select(tally => PublishedPartyMajority.Of(tally.Party, tally.Yea, tally.Nay)).ToArray();
        return new(totals, majorities, tallies, partyBySourceId);
    }

    private static PublishedPartyTally[] Tallies(VoteMemberLine[] lines, Dictionary<string, string> partyBySourceId) =>
        lines.GroupBy(line => partyBySourceId[line.SourceId])
            .Select(group => new PublishedPartyTally
            {
                Party = group.Key,
                Yea = group.Count(line => line.KnownCast == VoteCastKind.Yea),
                Nay = group.Count(line => line.KnownCast == VoteCastKind.Nay),
                Present = group.Count(line => line.KnownCast == VoteCastKind.Present),
                NotVoting = group.Count(line => line.KnownCast == VoteCastKind.NotVoting),
                Other = group.Count(line => line.KnownCast is null)
            })
            .OrderBy(tally => tally.Party, StringComparer.Ordinal).ToArray();

    private static bool HasCompleteCandidateCasts(ImmutableArray<VoteFact> facts, VoteMemberLine[] lines)
    {
        var headerCounts = PublishedVote.CandidateCountsOf(facts.Select(fact => fact.Text));
        if (headerCounts is null) return false;
        var capturedCounts = lines.GroupBy(line => line.CastText, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return capturedCounts.Keys.All(headerCounts.ContainsKey) &&
            headerCounts.All(pair => capturedCounts.GetValueOrDefault(pair.Key) == pair.Value);
    }

    private static Dictionary<string, PublishedVoteTotals> SenateTotals(string recordId, ImmutableArray<VoteFact> facts)
    {
        var count = VoteFacts.ValueOf(facts, VoteFactPrefixes.Count)
            ?? throw new ArgumentException($"Vote {recordId} has no count line.");
        return new() { [""] = ParseCounts(recordId, count) };
    }

    private static Dictionary<string, PublishedVoteTotals> HouseTotals(string recordId, ImmutableArray<VoteFact> facts)
    {
        var totals = facts.Where(fact => fact.Text.StartsWith(VoteFactPrefixes.PartyTotal, StringComparison.Ordinal))
            .Select(fact => fact.Text[VoteFactPrefixes.PartyTotal.Length..].Split(": ", 2))
            .Select(parts => parts.Length == 2 ? parts : throw new ArgumentException($"Vote {recordId} has a party total line with no counts: {parts[0]}"))
            .ToDictionary(parts => parts[0], parts => ParseCounts(recordId, parts[1]), StringComparer.Ordinal);
        return totals.Count > 0 ? totals : throw new ArgumentException($"Vote {recordId} has no party total lines.");
    }

    private static void RequireLettersMatch(string recordId, VoteMemberLine[] lines, Dictionary<string, PublishedVoteTotals> partyTotals)
    {
        var actual = lines.GroupBy(line => line.SourceParty).ToDictionary(group => group.Key, group => TotalsOf(group), StringComparer.Ordinal);
        var empty = TotalsOf([]);
        if (partyTotals.Keys.Union(actual.Keys).Any(letter => partyTotals.GetValueOrDefault(letter, empty) != actual.GetValueOrDefault(letter, empty)))
            throw new ArgumentException($"Vote {recordId} has member lines that do not add up to its party total lines.");
    }

    private static PublishedVoteTotals ParseCounts(string recordId, string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in text.Split(", "))
        {
            var space = part.LastIndexOf(' ');
            if (space < 1 || !int.TryParse(part[(space + 1)..], out var number) || number < 0 || !counts.TryAdd(part[..space], number))
                throw new ArgumentException($"Vote {recordId} has a count that is not a single 'label number': {part}");
        }
        var totals = new PublishedVoteTotals
        {
            Yea = Take(counts, VoteCountLabels.Yea),
            Nay = Take(counts, VoteCountLabels.Nay),
            Present = Take(counts, VoteCountLabels.Present),
            NotVoting = Take(counts, VoteCountLabels.NotVoting)
        };
        return counts.Count == 0 ? totals : throw new ArgumentException($"Vote {recordId} has an unknown count label: {counts.Keys.First()}");
    }

    private static int Take(Dictionary<string, int> counts, string label) => counts.Remove(label, out var number) ? number : 0;

    private static PublishedVoteTotals TotalsOf(IEnumerable<VoteMemberLine> lines)
    {
        var counts = lines.ToLookup(line => line.Cast);
        return new()
        {
            Yea = counts[VoteCastKind.Yea].Count(),
            Nay = counts[VoteCastKind.Nay].Count(),
            Present = counts[VoteCastKind.Present].Count(),
            NotVoting = counts[VoteCastKind.NotVoting].Count()
        };
    }

    private static PublishedVoteTotals Sum(IEnumerable<PublishedVoteTotals> parts) => new()
    {
        Yea = parts.Sum(part => part.Yea),
        Nay = parts.Sum(part => part.Nay),
        Present = parts.Sum(part => part.Present),
        NotVoting = parts.Sum(part => part.NotVoting)
    };
}
