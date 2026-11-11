using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The words and figures of the vote page that its markup does not own. Each rule has one method.</summary>
public static class VotePageView
{
    public const string PartiesHeading = "How the parties voted";
    public const string ResultHeading = "Result";
    public const string BreakersHeading = "Members who voted against most of their party";
    public const string MembersHeading = "How each tracked member voted";
    public const string SourceHeading = "Check the source";
    public const string BillHeading = "The bill";
    public const string IssuesHeading = "Issues";

    public static string MemberNoun(PublishedChamber chamber) => chamber == PublishedChamber.Senate ? "senator" : "House member";

    public static string ResultWords(VotePageData vote) => PlainVoteWords.ResultWord(vote.Result) ?? vote.Result;

    public static string KindName(VotePageData vote) =>
        vote.Kind == PublishedVoteKind.Other && vote.KindCitation is null ? PlainVoteWords.OtherKind : PlainVoteWords.Kind(vote.Kind);

    /// <summary>The bar of all casts of the chamber. Null when the source gives no totals, as for a vote for a candidate.</summary>
    public static PartyBarData? ChamberBar(VotePageData vote) =>
        vote.Totals is { } totals && totals.Yea + totals.Nay + totals.Present + totals.NotVoting > 0
            ? new($"{vote.Chamber} members", PartyTone.Neutral, totals.Yea, totals.Nay, totals.Present, totals.NotVoting, 0)
            : null;

    public static string ChamberCaption(VotePageData vote, PartyBarData bar) =>
        $"Each bar counts the {vote.Chamber} members who voted Yes, No or not at all on {Moment.Day(vote.VotedOn)}. {bar.Total} members have a recorded position, including {bar.NotVoting} who did not vote.";

    /// <summary>One bar for each party group, from the whole-party counts of the exact revision. Empty when the release holds no party counts.</summary>
    public static IReadOnlyList<PartyBarData> PartyBars(VotePageData vote) =>
        [.. (vote.PartyTallies ?? []).OrderBy(tally => PartyRank(tally.Party)).ThenBy(tally => tally.Party, StringComparer.Ordinal)
            .Select(tally => new PartyBarData(PublishedPartyNames.PartyOf(vote.Chamber, tally.Party), ToneOf(tally.Party),
                tally.Yea, tally.Nay, tally.Present, tally.NotVoting, tally.Other))];

    private static int PartyRank(string party) => party switch
    {
        PublishedPartyMajority.Democrat => 0,
        PublishedPartyMajority.Republican => 1,
        _ => 2
    };

    private static PartyTone ToneOf(string party) => party switch
    {
        PublishedPartyMajority.Democrat => PartyTone.Democrat,
        PublishedPartyMajority.Republican => PartyTone.Republican,
        _ => PartyTone.Neutral
    };

    /// <summary>The members who voted against most of their party, for each major party. Only a split vote with party counts has them.</summary>
    public static IReadOnlyList<VoteBreakerGroup> Breakers(VotePageData vote)
    {
        if (vote.Alignment != PublishedVoteAlignment.Split || vote.PartyTallies is null) return [];
        return [.. new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }
            .Select(party => BreakersOf(vote, party)).OfType<VoteBreakerGroup>()];
    }

    private static VoteBreakerGroup? BreakersOf(VotePageData vote, string party)
    {
        var tally = vote.PartyTallies!.SingleOrDefault(item => item.Party == party);
        if (tally is null || tally.Yea == tally.Nay) return null;
        var majority = tally.Yea > tally.Nay ? PublishedPartyMajority.Yea : PublishedPartyMajority.Nay;
        var members = vote.Members.Where(member => member.Party == party && PublishedPartyMajority.IsAgainstPartyMajority(party, member.Cast, majority))
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.OfficialId, StringComparer.Ordinal).ToArray();
        return new(party, PublishedPartyNames.PartyOf(vote.Chamber, party), members);
    }

    public static string BreakersHeadline(VoteBreakerGroup group) =>
        $"{group.Members.Length} tracked {(group.Members.Length == 1 ? "member" : "members")} voted against most of the party";

    public static string NoBreakers(VoteBreakerGroup group) => $"No tracked member of this party voted against most {group.Name}.";

    public static IReadOnlyList<VoteMemberData> Filtered(VotePageData vote, VoteMemberFilter filter) =>
        [.. vote.Members.Where(filter.Matches).OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.OfficialId, StringComparer.Ordinal)];

    public static IReadOnlyList<string> CastOptions(VotePageData vote) => [.. vote.Members.Select(member => member.Cast).Distinct().Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> PartyOptions(VotePageData vote) =>
        [.. vote.Members.Select(member => member.Party).OfType<string>().Distinct().Order(StringComparer.Ordinal)];

    /// <summary>The cited facts of the source fold. The result, the totals, and the date are shown once, each with its source line link.</summary>
    public static IReadOnlyList<Fact> Facts(VotePageData vote, VoteCitationAnchors anchors)
    {
        var noun = MemberNoun(vote.Chamber);
        var resultFact = vote.Facts.FirstOrDefault(fact => fact.Text == PublishedVote.ResultPrefix + vote.Result);
        var countFact = vote.Facts.FirstOrDefault(fact => fact.Text.StartsWith(PublishedVote.CountPrefix, StringComparison.Ordinal));
        var shown = new[] { resultFact, countFact };
        return
        [
            new Fact("Voted on", Moment.Day(vote.VotedOn)).CitedAt(anchors.Anchor(vote.VotedOnCitation)),
            new Fact("Kind", KindName(vote)) { TermKey = PublishedRecordLabels.VoteKindTermKey(vote.Kind) },
            .. vote.KindCitation is { } kind
                ? [new Fact("Question", PlainVoteWords.Question(vote.Kind, PublishedRecordLabels.VoteFactValue(kind.Quote))).CitedAt(anchors.Anchor(kind))]
                : Array.Empty<Fact>(),
            new Fact("Result", PlainVoteWords.Result(vote.Result)).CitedAt(resultFact is null ? null : anchors.Anchor(resultFact.Citation)),
            .. vote.Totals is { } totals
                ? [new Fact("Totals", $"Casts of all {noun}s: {PlainVoteWords.Totals(totals)}").CitedAt(countFact is null ? null : anchors.Anchor(countFact.Citation))]
                : Array.Empty<Fact>(),
            .. vote.Facts.Where(fact => !shown.Contains(fact) && fact.Citation != vote.VotedOnCitation && fact.Citation != vote.KindCitation)
                .Select(fact => new Fact(PublishedRecordLabels.VoteResultLabel(fact, noun + "s"), PublishedRecordLabels.VoteFactValue(fact.Text)).CitedAt(anchors.Anchor(fact.Citation))),
            new("Chamber", vote.Chamber.ToString()),
            new("Roll call", $"{vote.RollCall}, Congress {vote.Congress}, session {vote.Session}"),
            new("Data source", PublishedRecordLabels.VoteSource(vote.Source)),
            .. PublishedRecordLabels.Wording(vote.TextOrigin),
            new("Revision", vote.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ];
    }

    public static string SavedData(PublishedVoteSource source) => source switch
    {
        PublishedVoteSource.Voteview => "Voteview files",
        PublishedVoteSource.CongressGov => "Congress.gov API page",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Vote source has no saved data name.")
    };
}
