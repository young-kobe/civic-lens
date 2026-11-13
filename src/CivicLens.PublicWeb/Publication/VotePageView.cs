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
    public const string BillHeading = "The bill";
    public const string IssuesHeading = "Issues";
    public const string StatementsHeading = "Statements linked to this vote";
    public const string StatementsNoteTag = "How to read this";
    public const string StatementsNote = "A linked statement is the record's own words. It is not a reason Civic Lens gives for the vote.";
    public const string CastOnThisVote = "Cast on this vote";

    /// <summary>The most linked statements that the page lists, newest first.</summary>
    public const int StatementLimit = 10;

    /// <summary>Names the bill by its number. A vote that is not on a bill has no bill to name, so it has no intro.</summary>
    public static string? StatementsIntro(VotePageData vote) =>
        vote.BillType is { } type && vote.BillNumber is { } number ? $"Statements by members about {BillNumbers.Of(type, number)}." : null;

    /// <summary>Says how many linked statements the page lists when more are linked than it lists.</summary>
    public static string? StatementsCount(VotePageData vote) =>
        vote.StatementTotal > vote.Statements.Length ? $"The {vote.Statements.Length} newest of {vote.StatementTotal}." : null;

    /// <summary>
    /// The lines under the next step heading. When the bill became law, the law line is the only line, because it answers what happened next.
    /// Otherwise the line is the known next step, or the not-shown line.
    /// </summary>
    public static IReadOnlyList<string> NextStepLines(VotePageData vote, string? nextStep) =>
        BecameLaw(vote) is { } law ? [law] : [nextStep ?? NextStep.NotShown];

    /// <summary>The law line of the mockup. It shows only when the latest action of the bill makes it law, and only with the date that Civic Lens read it.</summary>
    private static string? BecameLaw(VotePageData vote) => vote.Bill is { PublicLaw: { } law, StatusDate: { } day, StatusReadAtUtc: { } read }
        ? $"The bill became law as Public Law {law} on {Moment.Day(day)}, according to Congress.gov, read on {Moment.Day(read)}."
        : null;

    public static string ResultWords(VotePageData vote) => PlainVoteWords.ResultWord(vote.Result) ?? vote.Result;

    public const string MarkLabel = "Half of Yes and No votes";

    public const string TwoThirdsMarkLabel = "Two-thirds of Yes and No votes";

    /// <summary>
    /// The share of the Yes and No casts that a vote kind needs, the one owner of that rule. A simple majority is known for the listed kinds only. Other and an unlisted kind need no mark.
    /// A suspension and a veto override need two-thirds. A cloture vote and the passage of a measure that may amend the Constitution draw no mark, because their needs differ.
    /// </summary>
    public static VoteThreshold ThresholdOf(VotePageData vote) => vote.Kind switch
    {
        PublishedVoteKind.Suspension or PublishedVoteKind.VetoOverride => VoteThreshold.TwoThirds,
        PublishedVoteKind.Passage => vote.WordingSource.PossibleConstitutionalPurpose ? VoteThreshold.None : VoteThreshold.Half,
        PublishedVoteKind.Rule or PublishedVoteKind.PreviousQuestion or PublishedVoteKind.MotionToRecommit or PublishedVoteKind.Amendment
            or PublishedVoteKind.ConferenceReport or PublishedVoteKind.Nomination or PublishedVoteKind.MotionToProceed => VoteThreshold.Half,
        _ => VoteThreshold.None
    };

    /// <summary>The bar of all casts of the chamber. Null when the source gives no totals, as for a vote for a candidate.</summary>
    public static PartyBarData? ChamberBar(VotePageData vote) =>
        vote.Totals is { } totals && totals.Yea + totals.Nay + totals.Present + totals.NotVoting > 0
            ? CastBar($"{vote.Chamber} members", PartyTone.Neutral, totals.Yea, totals.Nay, totals.Present, totals.NotVoting, 0, ThresholdOf(vote))
            : null;

    public static string ChamberCaption(VotePageData vote, PartyBarData bar) =>
        $"The bar counts the {vote.Chamber} members by their vote on {Moment.Day(vote.VotedOn)}: {PlainVoteWords.Yes}, {PlainVoteWords.No}, {PlainVoteWords.Present}, or {PlainVoteWords.NotVoting}. {bar.Total} members have a recorded position, including {bar.Segments.Single(segment => segment.Label == PlainVoteWords.NotVoting).Count} who did not vote.";

    /// <summary>One bar for each party group, from the whole-party counts of the exact revision. Empty when the release holds no party counts.</summary>
    public static IReadOnlyList<PartyBarData> PartyBars(VotePageData vote) =>
        [.. (vote.PartyTallies ?? []).OrderBy(tally => PartyTones.Rank(tally.Party)).ThenBy(tally => tally.Party, StringComparer.Ordinal)
            .Select(tally => CastBar(PublishedPartyNames.PartyOf(vote.Chamber, tally.Party), PartyTones.Of(tally.Party),
                tally.Yea, tally.Nay, tally.Present, tally.NotVoting, tally.Other, VoteThreshold.Half))];

    /// <summary>The casts of one group as party bar segments: Yes solid, No striped, Present dotted, Other gridded, and Not voting open. Present and Other show only when a member has them.</summary>
    public static PartyBarData CastBar(string name, PartyTone tone, int yes, int no, int present, int notVoting, int other, VoteThreshold threshold)
    {
        List<PartyBarSegment> segments = [new(PlainVoteWords.Yes, yes, tone, PartyBarFill.Solid), new(PlainVoteWords.No, no, tone, PartyBarFill.Stripes)];
        if (present > 0) segments.Add(new(PlainVoteWords.Present, present, PartyTone.Quiet, PartyBarFill.Dots));
        if (other > 0) segments.Add(new(PlainVoteWords.OtherCast, other, PartyTone.Quiet, PartyBarFill.Grid));
        segments.Add(new(PlainVoteWords.NotVoting, notVoting, PartyTone.Quiet, PartyBarFill.Open));
        return new(name, segments, MarkOf(threshold, yes + no));
    }

    private static PartyBarMark? MarkOf(VoteThreshold threshold, int yesAndNo) => yesAndNo <= 0 ? null : threshold switch
    {
        VoteThreshold.Half => new(MarkLabel, yesAndNo / 2.0),
        VoteThreshold.TwoThirds => new(TwoThirdsMarkLabel, yesAndNo * 2.0 / 3.0),
        _ => null
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
        if (tally is null || PublishedPartyMajority.Of(party, tally.Yea, tally.Nay).Majority is not { } majority) return null;
        var members = ByName(vote.Members.Where(member => member.Party == party && PublishedPartyMajority.IsAgainstPartyMajority(party, member.Cast, majority)));
        return new(party, PublishedPartyNames.PartyOf(vote.Chamber, party), [.. members]);
    }

    public static IReadOnlyList<VoteMemberData> Filtered(VotePageData vote, VoteMemberFilter filter) =>
        [.. ByName(vote.Members.Where(filter.Matches))];

    private static IOrderedEnumerable<VoteMemberData> ByName(IEnumerable<VoteMemberData> members) =>
        members.OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.OfficialId, StringComparer.Ordinal);

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
