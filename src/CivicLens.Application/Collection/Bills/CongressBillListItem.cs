using System.Globalization;

namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillListItem(int Congress, string Type, int Number, DateOnly UpdateDate)
{
    private const string DetailQuery = "?format=json";
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
        { "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres" };

    public static bool IsKnownType(string type) => KnownTypes.Contains(type);

    public static bool IsDetailUrl(string sourceUrl, string url)
    {
        var prefix = sourceUrl + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(DetailQuery, StringComparison.Ordinal))
            return false;
        var parts = url[prefix.Length..^DetailQuery.Length].Split('/');
        return parts.Length == 3 && IsKnownType(parts[1]) &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var congress) &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            new CongressBillListItem(congress, parts[1], number, default).DetailUrl(sourceUrl) == url;
    }

    public string DetailUrl(string sourceUrl) => $"{sourceUrl}/{Congress}/{Type}/{Number}{DetailQuery}";
}
