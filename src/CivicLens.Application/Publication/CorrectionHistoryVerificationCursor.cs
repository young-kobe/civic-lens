using System.Globalization;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryVerificationCursor(string SelectionFingerprint, int ReleaseNumber, PublishedRecordKind Kind, string RecordId)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
        $"1|{SelectionFingerprint}|{ReleaseNumber.ToString(CultureInfo.InvariantCulture)}|{PublishedRecordReference.NameOf(Kind)}|{RecordId}"))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static CorrectionHistoryVerificationCursor? Parse(string? value)
    {
        if (value is null) return null;
        try
        {
            if (value.Length is 0 or > 1024) throw Invalid();
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|', 5);
            if (parts.Length != 5 || parts[0] != "1" || parts[1].Length != 64 ||
                parts[1].Any(character => !Uri.IsHexDigit(character)) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var release) || release < 1)
                throw Invalid();
            var reference = PublishedRecordReference.Parse($"{parts[3]}:{parts[4]}");
            if (!PublicationProtocol.IsRecordId(reference.Kind, reference.RecordId)) throw Invalid();
            var cursor = new CorrectionHistoryVerificationCursor(parts[1], release, reference.Kind, reference.RecordId);
            if (cursor.Encode() != value) throw Invalid();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static ArgumentException Invalid() => new("Correction history verification cursor is not valid.", "cursor");
}
