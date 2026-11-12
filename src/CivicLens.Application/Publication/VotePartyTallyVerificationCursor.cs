using System.Globalization;

namespace CivicLens.Application.Publication;

/// <summary>The last revision that a tally verification page examined. It reads as <c>revision:recordId</c>, and the record ID may hold a colon.</summary>
public sealed record VotePartyTallyVerificationCursor(string RecordId, int RevisionNumber)
{
    public string Encode() => $"{RevisionNumber.ToString(CultureInfo.InvariantCulture)}:{RecordId}";

    public static VotePartyTallyVerificationCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split(':', 2);
        if (parts.Length != 2 || parts[1].Length == 0 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw new ArgumentException("Party tally verification cursor is not valid.", "cursor");
        var cursor = new VotePartyTallyVerificationCursor(parts[1], revision);
        return cursor.Encode() == value ? cursor : throw new ArgumentException("Party tally verification cursor is not valid.", "cursor");
    }
}
