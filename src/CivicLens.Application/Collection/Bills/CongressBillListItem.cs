using System.Globalization;

namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillListItem(int Congress, string Type, int Number, DateOnly UpdateDate)
{
    private const string DetailQuery = "?format=json";
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
        { "hr", "s", "hjres", "sjres", "hconres", "sconres", "hres", "sres" };

    public static bool IsKnownType(string type) => KnownTypes.Contains(type);

    public static string? NormalizeType(string type)
    {
        var lowered = type.ToLowerInvariant();
        return IsKnownType(lowered) ? lowered : null;
    }

    public static bool IsDetailUrl(string sourceUrl, string url) => TryReadDetailUrl(sourceUrl, url) is not null;

    public static CongressBillListItem? TryReadDetailUrl(string sourceUrl, string url)
    {
        var prefix = sourceUrl + "/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal) || !url.EndsWith(DetailQuery, StringComparison.Ordinal))
            return null;
        var parts = url[prefix.Length..^DetailQuery.Length].Split('/');
        return parts.Length == 3 ? TryReadIdentity(parts[0], parts[1], parts[2]) : null;
    }

    internal static CongressBillListItem? TryReadIdentity(string congress, string type, string number)
    {
        if (!IsKnownType(type) ||
            !int.TryParse(congress, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCongress) ||
            !int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedNumber))
            return null;
        var identity = new CongressBillListItem(parsedCongress, type, parsedNumber, default);
        return identity.IsIdentityText(congress, number) ? identity : null;
    }

    public bool IsSameBillAs(CongressBillListItem other) =>
        Congress == other.Congress && Type == other.Type && Number == other.Number;

    public string DetailUrl(string sourceUrl) => $"{sourceUrl}/{Congress}/{Type}/{Number}{DetailQuery}";

    private bool IsIdentityText(string congress, string number) =>
        congress == Congress.ToString(CultureInfo.InvariantCulture) &&
        number == Number.ToString(CultureInfo.InvariantCulture);
}
