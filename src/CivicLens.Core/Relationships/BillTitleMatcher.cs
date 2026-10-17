using System.Text.RegularExpressions;

namespace CivicLens.Core.Relationships;

public static partial class BillTitleMatcher
{
    public const int MinimumWords = 3;

    public static string? KeyOf(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var key = TrailingYear().Replace(LeadingThe().Replace(title.Trim(), ""), "");
        var words = key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length < MinimumWords ? null : string.Join(' ', words);
    }

    public static (int Start, int Length)? Find(string text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(key);
        for (var start = text.IndexOf(key, StringComparison.OrdinalIgnoreCase); start >= 0;
            start = text.IndexOf(key, start + 1, StringComparison.OrdinalIgnoreCase))
            if (IsWordBoundary(text, start - 1) && IsWordBoundary(text, start + key.Length)) return (start, key.Length);
        return null;
    }

    private static bool IsWordBoundary(string text, int index) =>
        index < 0 || index >= text.Length || !char.IsLetterOrDigit(text[index]);

    [GeneratedRegex(@"^the\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingThe();

    [GeneratedRegex(@"\s+of\s+\d{4}\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingYear();
}
