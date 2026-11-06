using CivicLens.Application.Publication;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedBoardView
{
    public const string TabsLabel = "Show the voting boards of a chamber and party";

    public const string ShareOrder = "Highest share first";

    public const string ShareSortName = "Share";

    public const string MostMissedOrder = "Most missed first";

    public const string TooFewTitle = "Too few split votes yet";

    public const string LeftOutTitle = "What this page leaves out";

    public const string Cause = "The page does not give the cause of a missed vote. A member can miss a vote for illness, a death in the family, a campaign, or other reasons.";

    public const string ReadingNote = "A vote against the party is not a mistake. A vote with the party is not a sign of good work. The lists show counts of votes and nothing more.";

    public const string Intro = "Code builds these lists from the roll call votes on this site. No model is used. An independent who caucuses with the party counts with that party.";

    public const string OfficialsLede = "Lists of the members of Congress who voted against their party most often, voted with it most often, and missed the most votes. There is one page for each chamber and party.";

    public static string ClosestTitle(ClosestVote closest) =>
        $"Closest vote, {Moment.Day(closest.WindowStart)} to {Moment.Day(closest.WindowEnd)}";

    public const string ClosestRule = "The card looks at the substantive roll call votes of the House and the Senate in which both the Democrats and the Republicans voted. It leaves out procedural votes, such as rules and motions. The gap ignores thresholds such as 60 votes in the Senate or two thirds of a chamber.";

    public static string VoteGap(ClosestVote closest) => closest.Gap switch
    {
        0 => $"Roll call on {Moment.Day(ReleaseRecordOrder.Day(closest.Entry))}. The Yes and No counts are equal.",
        var gap => $"Roll call on {Moment.Day(ReleaseRecordOrder.Day(closest.Entry))}. The Yes and No counts differ by {gap} {PublishedOfficialView.Noun(gap, "vote")}."
    };

    public static string VotePool(ClosestVote closest) => closest.VotesInWindow == 1
        ? "This is the only substantive roll call vote with counts in these dates."
        : $"The pick is the closest of {closest.VotesInWindow} substantive roll call votes with counts in these dates. The gap is the difference between the Yes count and the No count.";

    public const string AgreementCaption = "Votes where both parties agreed, by kind of vote";

    public const string AgreementKindHeader = "Kind of vote";

    public const string AgreementTotalLabel = "All substantive votes";

    public const string AgreementCaution = "That the parties voted the same way is about the question of each vote. It does not show that they agree on an issue.";

    public const string AgreementLinkText = "How this count works";

    public const string AgreementId = "parties-agreed";

    public const string AgreementAnchor = "#" + AgreementId;

    public static string AgreementTitle(ChamberAgreement agreement) =>
        $"When both parties agreed ({CongressCalendar.Ordinal(agreement.Congress)} Congress, {agreement.Chamber})";

    public static string AgreementLead(ChamberAgreement agreement) => agreement.Total == 0
        ? $"This site has no substantive {agreement.Chamber} roll call vote with both parties in the {CongressCalendar.Ordinal(agreement.Congress)} Congress."
        : $"On {agreement.Agreed} of the {agreement.Total} substantive {agreement.Chamber} roll call {PublishedOfficialView.Noun(agreement.Total, "vote")} on this site, {PublishedOfficialView.BothMajorities} voted the same way.";

    public static IReadOnlyList<AgreementRow> AgreementRows(ChamberAgreement agreement) =>
    [
        .. agreement.Kinds.Select(kind => new AgreementRow(PlainVoteWords.Kind(kind.Kind), $"{kind.Agreed} of {kind.Total}")),
        new(AgreementTotalLabel, $"{agreement.Agreed} of {agreement.Total}")
    ];

    public static string AgreementSplit(ChamberAgreement agreement) => $"On {agreement.Split} of the {agreement.Total}, the two parties took opposite sides.";

    public static string AgreementMixed(ChamberAgreement agreement) => $"On {agreement.Mixed} of the {agreement.Total}, one party had no majority, as in a tie.";

    public static bool ShowsNominationNote(ChamberAgreement agreement) => agreement.Nominations * 2 > agreement.Total;

    public static string NominationNote(ChamberAgreement agreement) =>
        $"Most of these {agreement.Total} votes are on nominations. Look at each kind of vote, not only the total.";

    public static string PartyOf(OfficialBoard board) => PublishedOfficialView.PartyOf(board.Chamber, board.Party);

    public static string Title(OfficialBoard board) => $"{PartyOf(board)}: voting boards";

    public static string Lede(OfficialBoardPage page) => page.Congress is { } congress
        ? $"Three lists of the {PartyOf(page.Board)} in the {CongressCalendar.Ordinal(congress)} Congress. {Intro}"
        : $"No vote figures are in this release yet. {Intro}";

    public static string Description(OfficialBoardPage page) =>
        $"{PartyOf(page.Board)} ranked by votes against their party and by missed votes, with the counts behind each rank.";

    public static IReadOnlyList<FilterTab> Tabs(OfficialBoard? current) =>
        [.. OfficialBoard.All.Select(board => FilterTab.Link(PartyOf(board), board.PagePath, board == current))];

    public static string BreakersTitle(OfficialBoard board) => $"Voted against most {PartyOf(board)}";

    public static string LoyalistsTitle(OfficialBoard board) => $"Voted with most {PartyOf(board)}";

    public static string AgainstShareHeader(OfficialBoard board) => $"Share of split votes against most {PartyOf(board)}";

    public static string WithShareHeader(OfficialBoard board) => $"Share of split votes with most {PartyOf(board)}";

    public static string MissedTitle(OfficialBoard board) => $"Missed votes of the {PartyOf(board)}";

    public static string RankingRule(OfficialBoard board) =>
        $"The list has only members of the {PartyOf(board)} with at least {OfficialBoardPage.MinimumSplitVotes} split votes cast as Yes or No. A shorter record could put a member first by chance.";

    public static string NoRankedMembers(OfficialBoard board, int congress) =>
        $"No member of the {PartyOf(board)} has {OfficialBoardPage.MinimumSplitVotes} split votes cast in the {CongressCalendar.Ordinal(congress)} Congress.";

    public static string NoMissedList(OfficialBoard board) => $"No member of the {PartyOf(board)} has a roll call on this site.";

    public static string NewestRollCall(int congress, DateOnly day) =>
        $"The newest roll call counted here, in the {CongressCalendar.Ordinal(congress)} Congress, is on {Moment.Day(day)}. No count includes a later day.";

    public static string KeptCongress(int newer, int kept) =>
        $"The {CongressCalendar.Ordinal(newer)} Congress has no member with {OfficialBoardPage.MinimumSplitVotes} split votes yet, so this page keeps the {CongressCalendar.Ordinal(kept)} Congress.";

    public static string TooFew(int count) =>
        $"{count} {PublishedOfficialView.Noun(count, "member")} cast Yes or No on fewer than {OfficialBoardPage.MinimumSplitVotes} split votes. The lists do not rank {(count == 1 ? "this member" : "these members")}.";

    public static string SortShare(double share) => share.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    public static IEnumerable<string> LeftOut(OfficialBoardPage page)
    {
        if (page.WithoutVotes > 0)
            yield return $"{page.WithoutVotes} {PublishedOfficialView.Noun(page.WithoutVotes, "official")} with no vote figures in this release.";
        if (page.NotInCongress > 0 && page.Congress is { } congress)
            yield return $"{page.NotInCongress} {PublishedOfficialView.Noun(page.NotInCongress, "official")} with no vote figures in this chamber in the {CongressCalendar.Ordinal(congress)} Congress.";
        if (page.WithoutMajorParty > 0)
            yield return $"{page.WithoutMajorParty} {PublishedOfficialView.Noun(page.WithoutMajorParty, "official")} with vote figures in this Congress but no {PublishedPartyMajority.Democrat} or {PublishedPartyMajority.Republican} party on their votes, so no party majority applies.";
        yield return "Members of the other party. They are on the other page for this chamber.";
        yield return "Roll calls that are not on this site, and casts of Present or Not voting. The split vote counts hold only Yes and No casts.";
    }
}

internal sealed record AgreementRow(string Label, string Count);
