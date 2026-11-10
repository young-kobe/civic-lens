using System.Globalization;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record CorrectionHistoryBootstrapCursor(int ReleaseNumber, PublishedRecordKind Kind, string RecordId)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
        $"1|{ReleaseNumber.ToString(CultureInfo.InvariantCulture)}|{PublishedRecordReference.NameOf(Kind)}|{RecordId}"))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static CorrectionHistoryBootstrapCursor? Parse(string? value)
    {
        if (value is null) return null;
        try
        {
            if (value.Length is 0 or > 512) throw Invalid();
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|', 4);
            if (parts.Length != 4 || parts[0] != "1" ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var release) || release < 1)
                throw Invalid();
            var reference = PublishedRecordReference.Parse($"{parts[2]}:{parts[3]}");
            if (!PublicationProtocol.IsRecordId(reference.Kind, reference.RecordId)) throw Invalid();
            var cursor = new CorrectionHistoryBootstrapCursor(release, reference.Kind, reference.RecordId);
            if (cursor.Encode() != value) throw Invalid();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static ArgumentException Invalid() => new("Correction history cursor is not valid.", "cursor");
}
