using System.Globalization;

namespace CivicLens.Application.Paging;

/// <summary>Opaque keyset position: a direction plus the (time, id) key of the row the client last saw.</summary>
public sealed record PageCursor(PageDirection Direction, long Ticks, string Id)
{
    private const int MaximumLength = 256;
    private const int MaximumIdLength = 128;

    public static PageCursor? Parse(string? value, string parameterName = "cursor")
    {
        if (value is null) return null;
        if (value.Length is 0 or > MaximumLength) throw Invalid(parameterName);
        var parts = value.Split('.', 3);
        if (parts.Length != 3 || parts[0] is not ("n" or "o") ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            !IsValidId(parts[2]))
            throw Invalid(parameterName);
        return new(parts[0] == "n" ? PageDirection.Newer : PageDirection.Older, ticks, parts[2]);
    }

    public string Encode() =>
        string.Create(CultureInfo.InvariantCulture, $"{(Direction == PageDirection.Newer ? 'n' : 'o')}.{Ticks}.{Id}");

    private static bool IsValidId(string value) => value.Length is > 0 and <= MaximumIdLength &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static ArgumentException Invalid(string parameterName) =>
        new("Page cursor is not valid.", parameterName);
}
