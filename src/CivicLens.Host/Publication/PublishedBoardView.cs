using CivicLens.Application.Publication;
using CivicLens.Core.Relationships;
using CivicLens.Host.Components.Ui;
using CivicLens.Publication.Contracts;

namespace CivicLens.Host.Publication;

internal static class PublishedBoardView
{
    public const string TabsLabel = "Show the voting boards of a chamber and party";

    public const string BreakersOrder = "Highest share of votes against the party first";

    public const string LoyalistsOrder = "Highest share of votes with the party first";

    public const string MostMissedOrder = "Most missed votes first";

    public const string TooFewTitle = "Too few split votes yet";

    public const string LeftOutTitle = "What this page leaves out";

    public const string Cause = "The page does not give the cause of a missed vote. A member can miss a vote for illness, a death in the family, a campaign, or other reasons.";

    public const string ReadingNote = "A vote against the party is not a mistake. A vote with the party is not a sign of good work. The lists show counts of votes and nothing more.";

    public const string Intro = "Code builds these lists from the roll call votes on this site. No model is used. An independent who caucuses with the party counts with that party.";

    public const string OfficialsLede = "Lists of the members of Congress who voted against their party most often, voted with it most often, and missed the most votes. There is one page for each chamber and party.";

    public static string PartyOf(OfficialBoard board) => PublishedOfficialView.PartyOf(board.Chamber, board.Party);

    public static string Title(OfficialBoard board) => $"{PartyOf(board)}: voting boards";

    public static string Lede(OfficialBoardPage page) => page.Congress is { } congress
        ? $"Three lists of the {PartyOf(page.Board)} in the {CongressCalendar.Ordinal(congress)} Congress. {Intro}"
        : $"No roll call votes are on this site yet. {Intro}";

    public static string Description(OfficialBoardPage page) =>
        $"{PartyOf(page.Board)} ranked by votes against their party and by missed votes, with the counts behind each rank.";

    public static IReadOnlyList<FilterTab> Tabs(OfficialBoard? current) =>
        [.. OfficialBoard.All.Select(board => FilterTab.Link(PartyOf(board), board.PagePath, board == current))];

    public static string BreakersTitle(OfficialBoard board) => $"Voted against most {PartyOf(board)}";

    public static string LoyalistsTitle(OfficialBoard board) => $"Voted with most {PartyOf(board)}";

    public static string MissedTitle(OfficialBoard board) => $"Missed votes of the {PartyOf(board)}";

    public static string RankingRule(OfficialBoard board) =>
        $"The list has only members of the {PartyOf(board)} with at least {OfficialBoardPage.MinimumSplitVotes} split votes cast with Yea or Nay. A shorter record could put a member first by chance.";

    public static string NoRankedMembers(OfficialBoard board, int congress) =>
        $"No member of the {PartyOf(board)} has {OfficialBoardPage.MinimumSplitVotes} split votes cast in the {CongressCalendar.Ordinal(congress)} Congress.";

    public static string NoMissedList(OfficialBoard board) => $"No member of the {PartyOf(board)} has a roll call on this site.";

    public static string NewestRollCall(DateOnly day) => $"The newest roll call on this site in this chamber is on {Moment.Day(day)}. Every count stops on that day.";

    public static string TooFew(int count) =>
        $"{count} {PublishedOfficialView.Noun(count, "member")} cast Yea or Nay on fewer than {OfficialBoardPage.MinimumSplitVotes} split votes. The lists do not rank {(count == 1 ? "this member" : "these members")}.";

    public static string SortShare(double share) => share.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    public static IEnumerable<string> LeftOut(OfficialBoardPage page)
    {
        if (page.WithoutVotes > 0)
            yield return $"{page.WithoutVotes} {PublishedOfficialView.Noun(page.WithoutVotes, "official")} with no roll call votes on this site, such as officials who are not members of Congress.";
        if (page.InEarlierCongress > 0)
            yield return $"{page.InEarlierCongress} {PublishedOfficialView.Noun(page.InEarlierCongress, "official")} whose newest votes are in an earlier Congress.";
        if (page.WithoutMajorParty > 0)
            yield return $"{page.WithoutMajorParty} {PublishedOfficialView.Noun(page.WithoutMajorParty, "official")} with votes in this Congress but no {PublishedPartyMajority.Democrat} or {PublishedPartyMajority.Republican} party on those votes, so no party majority applies.";
        yield return "Members of the other parties and of the other chamber. They are on the other pages.";
        yield return "Roll calls that are not on this site, and casts of Present or Not Voting. The split vote counts hold only Yea and Nay casts.";
    }
}
