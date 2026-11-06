using System.Globalization;
using System.Text.RegularExpressions;

namespace CivicLens.Application.Actions;

/// <summary>The counts of dollar amounts and of dates or years in a piece of text.</summary>
public sealed record BillFigureCounts(int Amounts, int Dates);

/// <summary>The one owner of the dollar amount, year and date patterns. The text measure uses them to see whether the accepted sentences keep the
/// figures of the bill text. The resolver uses them through <see cref="BillFigureSet"/> to refuse a figure that the bill text lacks.
/// A dollar amount is "$" and digits, with comma groups of three digits, a decimal part, and a unit word (thousand, million, billion or trillion) as in "$2 billion", or digits
/// and a unit word before "dollars" as in "2 million dollars". Its value is the full number, so "$50 million" and "$50,000,000" are the same
/// amount. A year is a number from 1900 to 2099. A date is a year after a month name (with an optional day), after "fiscal year" or "fiscal years", or after a
/// deadline phrase ("not later than", "before", "after", "beginning", "ending", "by"). A bill citation such as "H.R. 2027" is no year (see <see cref="WithoutBillCitation"/>). A year after "Act of" or "Code of", and a number after
/// "U.S.C." or "section", is no date. The patterns are a measure, not a check of meaning.</summary>
public static partial class BillFigures
{
    private const string Month = "(?:January|February|March|April|May|June|July|August|September|October|November|December)";
    private const string Number = @"(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,6})?";
    private const string Unit = "thousand|million|billion|trillion";
    private const string YearPattern = @"(?:19|20)\d{2}";

    public static BillFigureCounts Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(Amount().Count(text), Dates(text).Count());
    }

    /// <summary>Each date of the text as it is written, without its deadline phrase. "Not later than September 30, 2027" is "September 30, 2027".</summary>
    private static IEnumerable<string> Dates(string text) => Date().Matches(text).Select(match => match.Groups["date"].Value);

    /// <summary>The dates of <paramref name="text"/> that no date of <paramref name="sentences"/> writes the same way, ignoring case. A date repeated in
    /// the text is listed once.</summary>
    public static IReadOnlyList<string> MissingDates(string text, string sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var held = Dates(sentences).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. Dates(text).Where(date => !held.Contains(date)).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Each amount of the text as it is written and as a full number.</summary>
    internal static IEnumerable<(string Written, decimal Value)> Amounts(string text) =>
        Amount().Matches(text).Select(match => (match.Value.Trim(), ValueOf(match)));

    /// <summary>Each year of the text, wherever it stands.</summary>
    internal static IEnumerable<string> Years(string text) => Year().Matches(text).Select(match => match.Value);

    /// <summary>The text with each citation of the bill itself, such as "H.R. 2027", "S. 2027" or "H.J. Res. 2027", replaced by a space. Only the
    /// token goes, so the same number written as a year elsewhere stays.</summary>
    internal static string WithoutBillCitation(string text, string billNumber) =>
        BillCitation().Replace(text, match => match.Groups["number"].Value == billNumber ? " " : match.Value);

    private static decimal ValueOf(Match amount)
    {
        var number = decimal.Parse(amount.Groups["number"].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
        return number * (amount.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "thousand" => 1_000m,
            "million" => 1_000_000m,
            "billion" => 1_000_000_000m,
            "trillion" => 1_000_000_000_000m,
            _ => 1m
        });
    }

    [GeneratedRegex(@"\$(?<number>" + Number + ")(?: (?<unit>" + Unit + @")\b)?|\b(?<number>" + Number + @") (?:(?<unit>" + Unit + @") )?dollars\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Amount();

    [GeneratedRegex(@"\b[HS]\.(?: ?(?:J|Con)\.)?(?: ?R(?:es)?\.)? (?<number>\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex BillCitation();

    [GeneratedRegex(@"\b" + YearPattern + @"\b", RegexOptions.CultureInvariant)]
    private static partial Regex Year();

    [GeneratedRegex(@"\b(?:(?<date>" + Month + @" (?:\d{1,2}, )?" + YearPattern + @")|(?<date>(?i:fiscal years?) " + YearPattern + @")|(?i:not later than|before|after|beginning|ending|by) (?<date>(?:" + Month
        + @" (?:\d{1,2}, )?)?" + YearPattern + @"))\b", RegexOptions.CultureInvariant)]
    private static partial Regex Date();
}
