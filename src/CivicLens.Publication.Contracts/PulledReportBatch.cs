namespace CivicLens.Publication.Contracts;

/// <summary>A page of reports in sequence order. The next cursor is opaque. The puller sends it back as the acknowledgement and the start of the next page.</summary>
public sealed record PulledReportBatch
{
    public required PulledReport[] Reports { get; init; }
    public required string NextCursor { get; init; }

    public void Validate()
    {
        if (Reports is null || Reports.Length > ReaderReportProtocol.MaximumBatchSize || Reports.Any(report => report is null) ||
            string.IsNullOrEmpty(NextCursor) || NextCursor.Length > ReaderReportProtocol.MaximumCursorLength)
            throw new InvalidDataException("Pulled report batch is invalid.");
        foreach (var report in Reports) report.Validate();
    }
}
