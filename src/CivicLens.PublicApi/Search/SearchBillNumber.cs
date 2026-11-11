using System.Text.RegularExpressions;

namespace CivicLens.PublicApi.Search;

/// <summary>Reads a query that is only a bill number, such as "5334", "H.R. 5334", or "S.Res. 12". The type is the stored lower case form. A bare number has no type.</summary>
public static partial class SearchBillNumber
{
    private static readonly HashSet<string> BillTypes = new(StringComparer.Ordinal) { "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres" };

    [GeneratedRegex(@"^(?:(?<type>[a-z][a-z. ]{0,10}?)\s*)?(?<number>[1-9][0-9]{0,5})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string text, out string? billType, out int number)
    {
        billType = null;
        number = 0;
        if (Pattern().Match(text.Trim()) is not { Success: true } match) return false;
        if (match.Groups["type"].Success)
        {
            billType = match.Groups["type"].Value.Replace(".", "").Replace(" ", "").ToLowerInvariant();
            if (!BillTypes.Contains(billType)) return false;
        }
        number = int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}
