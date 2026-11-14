using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the time frame, agreement and caucus words that the static board pages, the server boards page and the full board pages share.</summary>
public static class BoardWords
{
    public const string ListsEyebrow = "Four lists, one for each chamber and party";

    public const string TabsLabel = "Party lists";

    public const string CodeBuilt = "Code builds every number from roll call votes on this site. No model is used.";

    /// <summary>The title of one board list: a panel of the boards page and the full board page.</summary>
    public static string ListTitle(PublishedChamber chamber, string party) => $"{PublishedPartyNames.PartyOf(chamber, party)}: voted against most of their party";

    public static string ListDescription(PublishedChamber chamber, string party) =>
        $"{PublishedPartyNames.PartyOf(chamber, party)} ranked by their share of split votes cast against most {PublishedPartyNames.Members(party)}, with the count and base of each.";

    public static string Eyebrow(int congress) => $"{CongressCalendar.Ordinal(congress)} Congress | {ListsEyebrow}";

    public static string NewestRollCall(int congress, DateOnly day) =>
        $"The newest roll call counted here, in the {CongressCalendar.Ordinal(congress)} Congress, is on {Moment.Day(day)}. No count includes a later day.";

    /// <summary>The kept Congress words of a static board page, which shows one board.</summary>
    public static string KeptCongress(int newer, int kept) => Kept(newer, kept, "this page");

    /// <summary>The kept Congress words of one list on the server boards page. The House and the Senate lists on that page can keep different Congresses.</summary>
    public static string KeptCongressInList(int newer, int kept) => Kept(newer, kept, "this list");

    private static string Kept(int newer, int kept, string place) =>
        $"The {CongressCalendar.Ordinal(newer)} Congress has no member with {OfficialBoardPage.MinimumSplitVotes} split votes yet, so {place} keeps the {CongressCalendar.Ordinal(kept)} Congress.";

    public const string CaucusRule = "An independent who caucuses with a party counts with that party.";

    public const string AgreementId = "parties-agreed";

    public const string AgreementTotalLabel = "All substantive votes";

    public const string AgreementCaution = "That the parties voted the same way is about the question of each vote. It does not show that they agree on an issue.";

    public static string AgreementLead(PublishedChamber chamber, int congress, int agreed, int total) => total == 0
        ? $"This site has no substantive {chamber} roll call vote with both parties in the {CongressCalendar.Ordinal(congress)} Congress."
        : $"On {agreed} of the {total} substantive {chamber} roll call {(total == 1 ? "vote" : "votes")} on this site, {VoteWords.BothMajorities} voted the same way.";

    public static string AgreementSplit(int split, int total) => $"On {split} of the {total}, the two parties took opposite sides.";

    public static string AgreementMixed(int mixed, int total) => $"On {mixed} of the {total}, one party had no majority, as in a tie.";

    public static bool ShowsNominationNote(int nominations, int total) => nominations * 2 > total;

    public static string NominationNote(int total) => $"Most of these {total} votes are on nominations. Look at each kind of vote, not only the total.";
}
