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

    private VoteTally(PublishedVoteTotals? totals, PublishedPartyMajority[] partyMajorities, Dictionary<string, string> partyBySourceId)
    {
        Totals = totals;
        PartyMajorities = partyMajorities;
        this.partyBySourceId = partyBySourceId;
    }

    public PublishedVoteTotals? Totals { get; }

    public PublishedPartyMajority[] PartyMajorities { get; }

    public string PartyOf(string sourceId) => partyBySourceId[sourceId];

    public static VoteTally Read(string recordId, Chamber chamber, ImmutableArray<VoteFact> facts, string membersText, DateOnly day,
        VoteParties parties)
    {
        var lines = membersText.Split('\n').Where(line => line.StartsWith(VoteMemberLine.Prefix, StringComparison.Ordinal))
            .Select(VoteMemberLine.Parse).ToArray();
        var partyBySourceId = lines.ToDictionary(line => line.SourceId, line => parties.PartyOn(line, chamber, day), StringComparer.Ordinal);
        if (VoteFacts.Find(facts, VoteFactPrefixes.CandidateTotal) is not null) return new(null, [], partyBySourceId);

        var partyTotals = chamber == Chamber.Senate ? SenateTotals(recordId, facts) : HouseTotals(recordId, facts);
        var totals = Sum(partyTotals.Values);
        if (TotalsOf(lines) != totals)
            throw new ArgumentException($"Vote {recordId} has member lines that do not add up to its total lines.");
        if (chamber == Chamber.House) RequireLettersMatch(recordId, lines, partyTotals);

        var majorities = lines.GroupBy(line => partyBySourceId[line.SourceId])
            .Select(group => PublishedPartyMajority.Of(group.Key, group.Count(line => line.Cast == VoteCastKind.Yea),
                group.Count(line => line.Cast == VoteCastKind.Nay)))
            .OrderBy(majority => majority.Party, StringComparer.Ordinal).ToArray();
        return new(totals, majorities, partyBySourceId);
    }

    private static Dictionary<string, PublishedVoteTotals> SenateTotals(string recordId, ImmutableArray<VoteFact> facts)
    {
        var count = VoteFacts.Find(facts, VoteFactPrefixes.Count)
            ?? throw new ArgumentException($"Vote {recordId} has no count line.");
        return new() { [""] = ParseCounts(recordId, count.Text[VoteFactPrefixes.Count.Length..]) };
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
            Yea = Take(counts, "Yea"),
            Nay = Take(counts, "Nay"),
            Present = Take(counts, "Present"),
            NotVoting = Take(counts, "Not voting")
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
