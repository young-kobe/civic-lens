using System.Globalization;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Binds a verification walk to one release: its number, its directory name, the last record done, and the failures already found in it.
/// A null <paramref name="After"/> means the walk starts at the first record of the release.
/// </summary>
public sealed record CorrectionHistoryVerificationCursor(int ReleaseNumber, string DirectoryName, PublishedRecordReference? After = null,
    int PriorFailures = 0)
{
    private const int MaximumLength = 1024;
    private const int MaximumDirectoryLength = 128;

    public string Encode()
    {
        var position = After is null ? "|" : $"{PublishedRecordReference.NameOf(After.Kind)}|{After.RecordId}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"2|{ReleaseNumber.ToString(CultureInfo.InvariantCulture)}|{DirectoryName}|{PriorFailures.ToString(CultureInfo.InvariantCulture)}|{position}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static CorrectionHistoryVerificationCursor? Parse(string? value)
    {
        if (value is null) return null;
        try
        {
            if (value.Length is 0 or > MaximumLength) throw Invalid();
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|', 6);
            if (parts.Length != 6 || parts[0] != "2" || !IsDirectoryName(parts[2]) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var release) || release < 1 ||
                !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var failures))
                throw Invalid();
            var cursor = new CorrectionHistoryVerificationCursor(release, parts[2], ParsePosition(parts[4], parts[5]), failures);
            if (cursor.Encode() != value) throw Invalid();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static PublishedRecordReference? ParsePosition(string kind, string recordId)
    {
        if (kind.Length == 0 && recordId.Length == 0) return null;
        var reference = PublishedRecordReference.Parse($"{kind}:{recordId}");
        return PublicationProtocol.IsRecordId(reference.Kind, reference.RecordId) ? reference : throw Invalid();
    }

    private static bool IsDirectoryName(string value) => value.Length is > 0 and <= MaximumDirectoryLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static ArgumentException Invalid() => new("Correction history verification cursor is not valid.", "cursor");
}
