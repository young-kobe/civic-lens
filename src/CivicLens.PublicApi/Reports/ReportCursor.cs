using System.Globalization;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Reports;

/// <summary>
/// Where the next report page starts: the epoch of this database and the last sequence that the puller holds.
/// A cursor of another epoch means that the database was reset, so the pull starts again from the first report.
/// </summary>
public sealed record ReportCursor(Guid Epoch, long Sequence)
{
    private const string EpochFormat = "N";

    public static ReportCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split('.');
        if (value.Length > ReaderReportProtocol.MaximumCursorLength || parts.Length != 2 ||
            !Guid.TryParseExact(parts[0], EpochFormat, out var epoch) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            throw new ArgumentException("Report cursor is not valid.", ReaderReportProtocol.AfterParameter);
        return new(epoch, sequence);
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Epoch.ToString(EpochFormat)}.{Sequence}");
}
