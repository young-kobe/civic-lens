using System.Globalization;
using CivicLens.Core.Review;

namespace CivicLens.Application.Paging;

public sealed record PageCursor(PageDirection Direction, long Ticks, string Id, int? Rank = null)
{
    private const int MaximumLength = 256;
    private const int MaximumIdLength = 128;

    public static PageCursor? Parse(string? value, string parameterName = "cursor")
    {
        if (value is null) return null;
        if (value.Length is 0 or > MaximumLength) throw Invalid(parameterName);
        var parts = value.Split('.');
        if (parts.Length is not (3 or 4) || parts[0] is not ("n" or "o") || !IsValidId(parts[^1]) ||
            !long.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            throw Invalid(parameterName);
        var direction = parts[0] == "n" ? PageDirection.Newer : PageDirection.Older;
        if (parts.Length == 3) return new(direction, ticks, parts[2]);
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rank) ||
            rank is < ReviewPriority.Highest or > ReviewPriority.Lowest)
            throw Invalid(parameterName);
        return new(direction, ticks, parts[3], rank);
    }

    public string Encode()
    {
        var direction = Direction == PageDirection.Newer ? 'n' : 'o';
        return Rank is { } rank
            ? string.Create(CultureInfo.InvariantCulture, $"{direction}.{rank}.{Ticks}.{Id}")
            : string.Create(CultureInfo.InvariantCulture, $"{direction}.{Ticks}.{Id}");
    }

    public static void RequireRank(PageCursor? cursor, bool ranked)
    {
        if (cursor is not null && (cursor.Rank is not null) != ranked) throw Invalid("cursor");
    }

    private static bool IsValidId(string value) => value.Length is > 0 and <= MaximumIdLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static ArgumentException Invalid(string parameterName) =>
        new("Page cursor is not valid.", parameterName);
}
