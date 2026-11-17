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
    public const string ZipMembersHeading = "Your members on this vote";

    /// <summary>The source API for a vote's member positions, when the browser API has a configured root.</summary>
    public static string? MembersApiUrl(string recordId, string? browserApiRoot) => browserApiRoot is null
        ? null
        : $"{browserApiRoot}/v1/votes/{recordId}/members";

    /// <summary>The line that the members card shows to a reader with no saved ZIP. The device fills the card from the private fragment when a ZIP is saved.</summary>
    public static string ZipMembersPrompt(PublishedChamber chamber) => chamber == PublishedChamber.Senate
        ? "Enter your ZIP to see how your senators voted."
        : "Enter your ZIP to see how your House member voted.";

    /// <summary>The party of the member on the vote and the seat that the member holds now. A member with no recorded position has no party on the vote.</summary>
    public static string ZipMemberSeat(VoteZipMemberData member) =>
        member.Cast is null ? $"{member.SeatTitle}." : $"{member.Party ?? VoteWords.NoKnownParty}. {member.SeatTitle}.";

    /// <summary>The party line of the member from <see cref="VoteZipMemberData.BreaksParty"/>, or null when the vote gives the member no party line.</summary>
    public static string? ZipMemberPartyLine(VoteZipMemberData member) => (member.BreaksParty, member.Party) switch
    {
        (true, { } party) => "Voted " + VoteBreakers.AgainstMost(PublishedPartyNames.Members(party)),
        (false, { } party) => "Voted " + VoteBreakers.WithMost(PublishedPartyNames.Members(party)),
        _ => null
    };

    public static string ZipMemberNoPosition(VoteZipMemberData member) => $"{member.Name} has no recorded position on this vote.";

    /// <summary>Says that the members of the other chamber do not vote on this roll call.</summary>
    public static string OtherChamberNote(PublishedChamber chamber, int count) => (chamber, count) switch
    {
        (PublishedChamber.Senate, 1) => "Your House member does not vote in the Senate.",
        (PublishedChamber.Senate, _) => "Your House members do not vote in the Senate.",
        (_, 1) => "Your senator does not vote in the House.",
        _ => "Your senators do not vote in the House."
    };

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
    public static PartyBarData? ChamberBar(VotePageData vote) => vote.Totals is { } totals
        ? ChamberBar(vote.Chamber, totals.Yea, totals.Nay, totals.Present, totals.NotVoting, ThresholdOf(vote))
        : null;

    /// <summary>The one owner of the chamber bar that vote pages and vote lists draw from the published counts. Null when no member has a cast.</summary>
    public static PartyBarData? ChamberBar(PublishedChamber chamber, int yes, int no, int present, int notVoting, VoteThreshold threshold) =>
        yes + no + present + notVoting > 0 ? CastBar($"{chamber} members", PartyTone.Neutral, yes, no, present, notVoting, 0, threshold) : null;

    public static string ChamberCaption(VotePageData vote, PartyBarData bar) =>
        $"The bar counts the {vote.Chamber} members by their vote on {Moment.Day(vote.VotedOn)}: {PlainVoteWords.Yes}, {PlainVoteWords.No}, {PlainVoteWords.Present}, or {PlainVoteWords.NotVoting}. {bar.Total} members have a recorded position, including {bar.Segments.Single(segment => segment.Label == PlainVoteWords.NotVoting).Count} who did not vote.";

    /// <summary>One bar for each party group, from the whole-party counts of the exact revision. Empty when the release holds no party counts.
    /// A party bar has no mark, because the votes needed to pass count the whole chamber, not one party.</summary>
    public static IReadOnlyList<PartyBarData> PartyBars(VotePageData vote) =>
        [.. (vote.PartyTallies ?? []).OrderBy(tally => PartyTones.Rank(tally.Party)).ThenBy(tally => tally.Party, StringComparer.Ordinal)
            .Select(tally => CastBar(PublishedPartyNames.PartyOf(vote.Chamber, tally.Party), PartyTones.Of(tally.Party),
                tally.Yea, tally.Nay, tally.Present, tally.NotVoting, tally.Other, VoteThreshold.None))];

    /// <summary>The casts of one group as party bar segments: Yes solid, No striped, Present dotted, Other gridded, and Not voting open. Present and Other show only when a member has them.</summary>
    public static PartyBarData CastBar(string name, PartyTone tone, int yes, int no, int present, int notVoting, int other, VoteThreshold threshold)
    {
        List<PartyBarSegment> segments = [new(PlainVoteWords.Yes, yes, tone, PartyBarFill.Solid), new(PlainVoteWords.No, no, tone, PartyBarFill.Stripes)];
        if (present > 0) segments.Add(new(PlainVoteWords.Present, present, PartyTone.Quiet, PartyBarFill.Dots));
        if (other > 0) segments.Add(new(PlainVoteWords.OtherCast, other, PartyTone.Quiet, PartyBarFill.Grid));
        segments.Add(new(PlainVoteWords.NotVoting, notVoting, PartyTone.Quiet, PartyBarFill.Open));
        return new(name, segments, MarkOf(threshold, yes + no));
    }

    /// <summary>The mark of the whole number of Yes votes that the vote needs: more than half, or at least two-thirds, of the Yes and No casts. A tie fails, so half alone is not enough.</summary>
    private static PartyBarMark? MarkOf(VoteThreshold threshold, int yesAndNo) => yesAndNo <= 0 ? null : threshold switch
    {
        VoteThreshold.Half => new(NeededLabel("more than half", yesAndNo), yesAndNo / 2 + 1),
        VoteThreshold.TwoThirds => new(NeededLabel("two-thirds", yesAndNo), (2 * yesAndNo + 2) / 3),
        _ => null
    };

    private static string NeededLabel(string share, int yesAndNo) => $"Yes votes needed: {share} of the {PublishedWording.Count(yesAndNo)} Yes and No votes";

    /// <summary>
    /// The members who voted against most of their party, for each major party, as <see cref="VoteMemberData.BreaksParty"/> gives them. A party has a group only when
    /// one of its tracked members has a party line on this vote, so a vote that is not split has no groups.
    /// </summary>
    public static IReadOnlyList<VoteBreakerGroup> Breakers(VotePageData vote) =>
        [.. new[] { PublishedPartyMajority.Democrat, PublishedPartyMajority.Republican }.Select(party => BreakersOf(vote, party)).OfType<VoteBreakerGroup>()];

    private static VoteBreakerGroup? BreakersOf(VotePageData vote, string party)
    {
        var lined = vote.Members.Where(member => member.Party == party && member.BreaksParty is not null).ToArray();
        return lined.Length == 0 ? null
            : new(party, PublishedPartyNames.PartyOf(vote.Chamber, party), [.. ByName(lined.Where(member => member.BreaksParty == true))]);
    }

    public static IReadOnlyList<VoteMemberData> Filtered(VotePageData vote, VoteMemberFilter filter) =>
        [.. ByName(vote.Members.Where(filter.Matches))];

    private static IOrderedEnumerable<VoteMemberData> ByName(IEnumerable<VoteMemberData> members) =>
        members.OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase).ThenBy(member => member.OfficialId, StringComparer.Ordinal);

    public static IReadOnlyList<string> CastOptions(VotePageData vote) => [.. vote.Members.Select(member => member.Cast).Distinct().Order(StringComparer.Ordinal)];

    public static IReadOnlyList<string> PartyOptions(VotePageData vote) =>
        [.. vote.Members.Select(member => member.Party).OfType<string>().Distinct().Order(StringComparer.Ordinal)];

    public static bool HasUnknownParty(VotePageData vote) => vote.Members.Any(member => member.Party is null);

    /// <summary>The one line under the headline that says what the vote is about. It is the bill line when the bill has one. Else it is
    /// the source description of the vote, word for word, unless the headline already holds it. Else the vote has none.</summary>
    public static VoteAbout? About(VotePageData vote)
    {
        if (vote.Bill?.Line is { } line) return new(line, FromBill: true);
        var description = vote.Facts.FirstOrDefault(fact => fact.Text.StartsWith(PublishedVote.DescriptionPrefix, StringComparison.Ordinal))
            ?.Text[PublishedVote.DescriptionPrefix.Length..].Trim();
        return string.IsNullOrEmpty(description) || vote.Headline.Contains(description.TrimEnd('.'), StringComparison.Ordinal)
            ? null : new(description, FromBill: false);
    }

    /// <summary>The facts of the source fold: the cited facts, then the identity of the roll call, the AI label, and the revision.</summary>
    public static IReadOnlyList<Fact> Facts(VotePageData vote, VoteCitationAnchors anchors) =>
    [
        .. VoteFacts.Cited(vote.FactsSource, anchors.Anchor),
        .. VoteFacts.Identity(vote.Chamber, vote.RollCall, vote.Congress, vote.Session, vote.Source),
        .. vote.Label is { } label ? [new Fact(PublishedRecordLabels.WordingFact, label) { AiLabelOrigin = vote.TextOrigin }] : Array.Empty<Fact>(),
        new("Revision", vote.RevisionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
    ];
}
