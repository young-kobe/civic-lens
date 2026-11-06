using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Records;

/// <summary>
/// Where the next page of related records starts. The path names the record, so the cursor holds the release and the sort key: the statement date, undated last, then the relationship record ID.
/// A client can forge any key. A forged key only moves the window over rows that are public anyway.
/// </summary>
public sealed record RelatedCursor(int Release, DateOnly? StatementDate, string LinkRecordId)
{
    private const string Tag = "r";
    private const string Undated = "n";

    public static RelatedCursor Of(int release, RelatedRecord last) => new(release, last.StatementDate, last.LinkRecordId);

    public static RelatedCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), parts[1] == Undated ? null : CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture,
        $"{Tag}.{Release}.{(StatementDate is { } date ? CursorFields.Format(date) : Undated)}.{LinkRecordId}");
}
