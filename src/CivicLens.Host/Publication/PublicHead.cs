using System.Text.RegularExpressions;

namespace CivicLens.Host.Publication;

internal static partial class PublicHead
{
    public const string SiteName = "Civic Lens";
    private const int MaximumTitleLength = 70;
    private const int MaximumDescriptionLength = 160;

    public static string DocumentTitle(string pageName, int page, int pageCount)
    {
        if (pageName == SiteName) return SiteName;
        var name = Excerpt(pageName, MaximumTitleLength);
        return pageCount > 1 ? $"{name}, page {page} of {pageCount} | {SiteName}" : $"{name} | {SiteName}";
    }

    public static string Description(string text) => Excerpt(text, MaximumDescriptionLength);

    private static string Excerpt(string text, int maximumLength)
    {
        var plain = Whitespace().Replace(text, " ").Trim();
        if (plain.Length <= maximumLength) return plain;
        var cut = plain.LastIndexOf(' ', maximumLength - 1);
        if (cut <= 0) cut = char.IsHighSurrogate(plain[maximumLength - 2]) ? maximumLength - 2 : maximumLength - 1;
        return plain[..cut].TrimEnd(',', ';', ':', '.') + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
