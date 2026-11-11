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

    public static string ResultWords(VotePageData vote) => PlainVoteWords.ResultWord(vote.Result) ?? vote.Result;

    public const string MarkLabel = "Half of Yes and No votes";

    /// <summary>True for a vote kind that needs more than half of the Yes and No casts. The chamber bar of such a vote draws no line until the owner approves the threshold wording.</summary>
    public static bool NeedsMoreThanMajority(PublishedVoteKind kind) => kind is PublishedVoteKind.Suspension or PublishedVoteKind.VetoOverride or PublishedVoteKind.Cloture;

    /// <summary>The bar of all casts of the chamber. Null when the source gives no totals, as for a vote for a candidate.</summary>
    public static PartyBarData? ChamberBar(VotePageData vote) =>
        vote.Totals is { } totals && totals.Yea + totals.Nay + totals.Present + totals.NotVoting > 0
            ? CastBar($"{vote.Chamber} members", PartyTone.Neutral, totals.Yea, totals.Nay, totals.Present, totals.NotVoting, 0, !NeedsMoreThanMajority(vote.Kind))
            : null;

    public static string ChamberCaption(VotePageData vote, PartyBarData bar) =>
        $"The bar counts the {vote.Chamber} members by their vote on {Moment.Day(vote.VotedOn)}: {PlainVoteWords.Yes}, {PlainVoteWords.No}, {PlainVoteWords.Present}, or {PlainVoteWords.NotVoting}. {bar.Total} members have a recorded position, including {vote.Totals?.NotVoting} who did not vote.";

    /// <summary>One bar for each party group, from the whole-party counts of the exact revision. Empty when the release holds no party counts.</summary>
    public static IReadOnlyList<PartyBarData> PartyBars(VotePageData vote) =>
        [.. (vote.PartyTallies ?? []).OrderBy(tally => PartyTones.Rank(tally.Party)).ThenBy(tally => tally.Party, StringComparer.Ordinal)
            .Select(tally => CastBar(PublishedPartyNames.PartyOf(vote.Chamber, tally.Party), PartyTones.Of(tally.Party),
                tally.Yea, tally.Nay, tally.Present, tally.NotVoting, tally.Other, true))];

    /// <summary>The casts of one group as party bar segments: Yes solid, No striped, Present dotted, Other gridded, and Not voting open. Present and Other show only when a member has them.</summary>
    public static PartyBarData CastBar(string name, PartyTone tone, int yes, int no, int present, int notVoting, int other, bool drawHalfLine)
    {
        List<PartyBarSegment> segments = [new(PlainVoteWords.Yes, yes, tone, PartyBarFill.Solid), new(PlainVoteWords.No, no, tone, PartyBarFill.Stripes)];
        if (present > 0) segments.Add(new(PlainVoteWords.Present, present, PartyTone.Quiet, PartyBarFill.Dots));
        if (other > 0) segments.Add(new("Other", other, PartyTone.Quiet, PartyBarFill.Grid));
        segments.Add(new(PlainVoteWords.NotVoting, notVoting, PartyTone.Quiet, PartyBarFill.Open));
        return new(name, segments, drawHalfLine && yes + no > 0 ? new(MarkLabel, (yes + no) / 2.0) : null);
    }

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
        if (tally is null || PublishedPartyMajority.Of(party, tally.Yea, tally.Nay).Majority is not { } majority) return null;
        var members = vote.Members.Where(member => member.Party == party && PublishedPartyMajority.IsAgainstPartyMajority(party, member.Cast, majority))
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.OfficialId, StringComparer.Ordinal).ToArray();
        return new(party, PublishedPartyNames.PartyOf(vote.Chamber, party), members);
    }

    public static IReadOnlyList<VoteMemberData> Filtered(VotePageData vote, VoteMemberFilter filter) =>
        [.. vote.Members.Where(filter.Matches).OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.OfficialId, StringComparer.Ordinal)];

    public static IReadOnlyList<string> CastOptions(VotePageData vote) => [.. vote.Members.Select(member => member.Cast).Distinct().Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> PartyOptions(VotePageData vote) =>
        [.. vote.Members.Select(member => member.Party).OfType<string>().Distinct().Order(StringComparer.Ordinal)];

    public static bool HasUnknownParty(VotePageData vote) => vote.Members.Any(member => member.Party is null);

    /// <summary>The facts of the source fold: the cited facts, then the identity of the roll call, the wording origin, and the revision.</summary>
    public static IReadOnlyList<Fact> Facts(VotePageData vote, VoteCitationAnchors anchors) =>
    [
        .. VoteFacts.Cited(vote.FactsSource, anchors.Anchor),
        .. VoteFacts.Identity(vote.Chamber, vote.RollCall, vote.Congress, vote.Session, vote.Source),
        .. PublishedRecordLabels.Wording(vote.TextOrigin),
        new("Revision", vote.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
    ];
}
