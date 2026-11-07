using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Statements;

public sealed record StatementCursor(int Release, DateOnly? StatementDate, string RecordId)
{
    private const string Tag = "t";
    private const string Undated = "n";
    public static StatementCursor Of(int release, StatementRow row) => new(release, row.StatementDate, row.RecordId);
    public static StatementCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), parts[1] == Undated ? null : CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }
    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{(StatementDate is { } date ? CursorFields.Format(date) : Undated)}.{RecordId}");
}
