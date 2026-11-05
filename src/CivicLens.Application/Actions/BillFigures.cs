using System.Text.RegularExpressions;

namespace CivicLens.Application.Actions;

/// <summary>The counts of dollar amounts and of dates or years in a piece of text.</summary>
public sealed record BillFigureCounts(int Amounts, int Dates);

/// <summary>The one owner of the dollar amount and date patterns. The text measure uses them to see whether the accepted sentences keep the
/// figures of the bill text. A dollar amount is "$" and digits, with commas, a decimal part, and a unit word as in "$2 billion", or digits
/// and a unit word before "dollars" as in "2 million dollars". A date is a year from 1900 to 2099, with an optional month and day before it,
/// as in "January 1, 2027" or "January 2027". "Fiscal year 2026" counts as the year 2026. The patterns are a measure, not a check: a number
/// that looks like a year, such as a section number, counts as a date.</summary>
public static partial class BillFigures
{
    public static BillFigureCounts Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(Amount().Count(text), Date().Count(text));
    }

    [GeneratedRegex(@"\$\d[\d,]*(?:\.\d+)?(?: (?:thousand|million|billion|trillion)\b)?|\b\d[\d,]*(?:\.\d+)? (?:(?:thousand|million|billion|trillion) )?dollars\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Amount();

    [GeneratedRegex(@"\b(?:(?:January|February|March|April|May|June|July|August|September|October|November|December) (?:\d{1,2}, )?)?(?:19|20)\d{2}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex Date();
}
