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

    public static string KeptCongress(int newer, int kept) =>
        $"The {CongressCalendar.Ordinal(newer)} Congress has no member with {OfficialBoardPage.MinimumSplitVotes} split votes yet, so this page keeps the {CongressCalendar.Ordinal(kept)} Congress.";
}
