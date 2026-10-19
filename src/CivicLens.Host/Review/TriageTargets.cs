using System.Globalization;
using CivicLens.Application.Review;

namespace CivicLens.Host.Review;

internal static class TriageTargets
{
    public static string Format(string recordId, int revisionNumber, int reviewStateVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"{recordId}:{revisionNumber}:{reviewStateVersion}");

    public static BulkApprovalTarget Parse(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var stateVersion))
            throw new ArgumentException("A selected record is not valid.");
        return new(parts[0], revision, stateVersion);
    }
}
