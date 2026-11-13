using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// Where the next page of one record kind of an official starts. Every kind sorts newest day first, then record ID, so one cursor serves all of them.
/// It holds the release, so a walk never mixes two releases. A client can forge any key. A forged key only moves the window over rows that are public anyway.
/// </summary>
public sealed record OfficialRecordCursor(int Release, DateOnly Day, string RecordId)
{
    private const string Tag = "r";

    public static OfficialRecordCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{CursorFields.Format(Day)}.{RecordId}");
}
