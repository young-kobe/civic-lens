using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Bills;

/// <summary>
/// Where the next page of bills starts. It holds the release, so a walk never mixes two releases, and the whole sort key, so the query needs no lookup.
/// A client can forge any key. A forged key only moves the window over rows that are public anyway.
/// </summary>
public sealed record BillCursor(int Release, DateOnly EventDate, string RecordId)
{
    private const string Tag = "b";

    public static BillCursor Of(int release, BillRow last) => new(release, last.EventDate, last.RecordId);

    public static BillCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{CursorFields.Format(EventDate)}.{RecordId}");
}
