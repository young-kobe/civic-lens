using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words of the issue tiles that Home and the issues page share.</summary>
public static class IssueTileWords
{
    /// <summary>The lede that names the one Congress of every counted tile, so no tile repeats it.</summary>
    public static string SharedCongress(int congress) =>
        $"Each tile counts the substantive votes and the bills tagged with the issue in the {CongressCalendar.Ordinal(congress)} Congress.";
}
