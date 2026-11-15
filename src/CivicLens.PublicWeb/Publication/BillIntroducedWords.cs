using CivicLens.PublicWeb.Components.Ui;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one owner of the words that print the introduced date of a bill: the bill lede, the introduction step, the "Added on" term of the original
/// cosponsors, and the bill rows of the official and record pages. The date is <c>public_api.bills.introduced_on</c>. It is null until the backfill
/// reaches the revision, and <see cref="Words"/> holds the one no-date branch. A page then prints no introduction date. The precedent is <c>BillTextWords</c>.
/// </summary>
public static class BillIntroducedWords
{
    /// <summary>The lede sentence, such as "Introduced 6 Apr 2026.", or null with no date.</summary>
    public static string? Lede(DateOnly? introduced) => Words(introduced, day => $"Introduced {day}.");

    /// <summary>The meta part of a bill row, such as "Introduced 6 Apr 2026", or null with no date.</summary>
    public static string? RowMeta(DateOnly? introduced) => Words(introduced, day => $"Introduced {day}");

    /// <summary>The detail of the introduction step: "6 Apr 2026, by Pat One", or only the date when the release tracks no sponsor. Empty with no date.</summary>
    public static string Step(DateOnly? introduced, string? sponsor) =>
        Words(introduced, day => sponsor is null ? day : $"{day}, by {sponsor}") ?? string.Empty;

    /// <summary>The term of the original cosponsors, such as "Added on 6 Apr 2026", or "Added on the day it was introduced" with no date.</summary>
    public static string AddedOn(DateOnly? introduced) => Words(introduced, day => $"Added on {day}") ?? "Added on the day it was introduced";

    private static string? Words(DateOnly? introduced, Func<string, string> words) => introduced is { } day ? words(Moment.Day(day)) : null;
}
