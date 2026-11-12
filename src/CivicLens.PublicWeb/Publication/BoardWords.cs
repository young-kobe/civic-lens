using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the time frame words that the static board pages and the server boards page share.</summary>
public static class BoardWords
{
    public const string ListsEyebrow = "Four lists, one for each chamber and party";

    public static string Eyebrow(int congress) => $"{CongressCalendar.Ordinal(congress)} Congress | {ListsEyebrow}";

    public static string NewestRollCall(int congress, DateOnly day) =>
        $"The newest roll call counted here, in the {CongressCalendar.Ordinal(congress)} Congress, is on {Moment.Day(day)}. No count includes a later day.";

    /// <summary>The kept Congress words of a static board page, which shows one board.</summary>
    public static string KeptCongress(int newer, int kept) => Kept(newer, kept, "this page");

    /// <summary>The kept Congress words of one list on the server boards page. The House and the Senate lists on that page can keep different Congresses.</summary>
    public static string KeptCongressInList(int newer, int kept) => Kept(newer, kept, "this list");

    private static string Kept(int newer, int kept, string place) =>
        $"The {CongressCalendar.Ordinal(newer)} Congress has no member with {OfficialBoardPage.MinimumSplitVotes} split votes yet, so {place} keeps the {CongressCalendar.Ordinal(kept)} Congress.";
}
