using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Reports;

public interface IReaderReportSource
{
    /// <summary>Pulls the page of reports after the cursor. A null cursor starts from the first report. The cursor is opaque.</summary>
    Task<PulledReportBatch> PullAsync(string? cursor, CancellationToken cancellationToken);
}
