using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>Where the next page of document changes starts. It holds the release and the whole sort key. A change with no date sorts after every dated change, as a statement with no date does.</summary>
public sealed record DocumentChangeCursor(int Release, DateOnly? ChangeDate, string RecordId)
{
    private const string Tag = "d";
    private const string Undated = "n";

    public static DocumentChangeCursor Of(int release, DocumentChangeListRow last) => new(release, last.ChangeDate, last.RecordId);

    public static DocumentChangeCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), parts[1] == Undated ? null : CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{(ChangeDate is { } date ? CursorFields.Format(date) : Undated)}.{RecordId}");
}
