using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Bills;

/// <summary>
/// Where the next page of bills starts. It holds the release, so a walk never mixes two releases, and the whole sort key, so the query needs no lookup.
/// The key is the latest action date, null for a bill with no stored status, and the record ID. A client can forge any key. A forged key only moves the window over rows that are public anyway.
/// The tag changed when the sort key changed from the event date, so a cursor of the old sort is refused as invalid.
/// </summary>
public sealed record BillCursor(int Release, DateOnly? LatestActionDate, string RecordId)
{
    private const string Tag = "bl";
    private const string NoAction = "n";

    public static BillCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), parts[1] == NoAction ? null : CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() =>
        string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{(LatestActionDate is { } date ? CursorFields.Format(date) : NoAction)}.{RecordId}");
}
