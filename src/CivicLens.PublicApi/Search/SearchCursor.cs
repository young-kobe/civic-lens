using System.Globalization;

namespace CivicLens.PublicApi.Search;

/// <summary>
/// Where the next page starts. It names the release, so every page of one search reads the same release.
/// A ranked cursor holds the bits of the rank, so the rank round-trips exactly. A dated cursor holds the sort date and the first publication time.
/// A sorted cursor holds only the record. The query finds the sort key of that record in the same release.
/// </summary>
public sealed record SearchCursor(int Release, string RecordId, float? Rank, DateOnly? SortDate, long? PublishedTicks)
{
    private const int MaximumLength = 200;
    private const string DateFormat = "yyyyMMdd";

    public static SearchCursor Ranked(int release, string recordId, float rank) => new(release, recordId, rank, null, null);

    public static SearchCursor Sorted(int release, string recordId) => new(release, recordId, null, null, null);

    public static SearchCursor Dated(int release, string recordId, DateOnly sortDate, long publishedTicks) =>
        new(release, recordId, null, sortDate, publishedTicks);

    public static SearchCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split('.');
        if (value.Length > MaximumLength || parts.Length is not (3 or 4 or 5) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var release))
            throw Invalid();
        var id = parts[^1];
        if (id.Length is 0 or > 64 || !id.All(char.IsAsciiHexDigitLower)) throw Invalid();
        if (parts is ["s", _, _]) return Sorted(release, id);
        if (parts is ["r", _, var bits, _] && int.TryParse(bits, NumberStyles.None, CultureInfo.InvariantCulture, out var rankBits) &&
            float.IsFinite(BitConverter.Int32BitsToSingle(rankBits)))
            return Ranked(release, id, BitConverter.Int32BitsToSingle(rankBits));
        if (parts is ["d", _, var date, var ticks, _] && DateOnly.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sortDate) &&
            long.TryParse(ticks, NumberStyles.None, CultureInfo.InvariantCulture, out var publishedTicks))
            return Dated(release, id, sortDate, publishedTicks);
        throw Invalid();
    }

    public bool IsSorted => Rank is null && SortDate is null;

    public string Encode() => this switch
    {
        { Rank: { } rank } => string.Create(CultureInfo.InvariantCulture, $"r.{Release}.{BitConverter.SingleToInt32Bits(rank)}.{RecordId}"),
        { IsSorted: true } => string.Create(CultureInfo.InvariantCulture, $"s.{Release}.{RecordId}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"d.{Release}.{SortDate!.Value.ToString(DateFormat, CultureInfo.InvariantCulture)}.{PublishedTicks}.{RecordId}")
    };

    private static ArgumentException Invalid() => new("Page cursor is not valid.", "cursor");
}
