using System.Globalization;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Http;

/// <summary>Reads the dot separated fields of a page cursor. A field that does not parse throws the invalid cursor <see cref="ArgumentException"/>.</summary>
internal static class CursorFields
{
    public const string DateFormat = "yyyyMMdd";
    private const int MaximumLength = 200;

    /// <summary>Returns the fields after the tag. The cursor must carry the tag and exactly <paramref name="fieldCount"/> fields, and at most <paramref name="maximumLength"/> characters.</summary>
    public static string[] Split(string value, string tag, int fieldCount, int maximumLength = MaximumLength)
    {
        var parts = value.Split('.');
        return value.Length <= maximumLength && parts.Length == fieldCount + 1 && parts[0] == tag ? parts[1..] : throw QueryValues.InvalidCursor();
    }

    public static int Number(string field) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : throw QueryValues.InvalidCursor();

    public static int Release(string field) => Number(field) is var release and >= 1 ? release : throw QueryValues.InvalidCursor();

    public static DateOnly Date(string field) =>
        DateOnly.TryParseExact(field, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw QueryValues.InvalidCursor();

    public static string RecordId(string field) => PublicationProtocol.IsAnyRecordId(field) ? field : throw QueryValues.InvalidCursor();

    public static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
