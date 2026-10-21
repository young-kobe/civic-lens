using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Reports.Persistence;

internal sealed class ReaderReportRow
{
    public Guid ReportId { get; set; }
    public string RecordId { get; set; } = "";
    public PublishedRecordKind Kind { get; set; }
    public int RevisionNumber { get; set; }
    public string Category { get; set; } = "";
    public string Text { get; set; } = "";
    public long ReceivedAtUtcTicks { get; set; }
    public ReaderReportStatus Status { get; set; }
    public string? Note { get; set; }
    public string? ResolvedBy { get; set; }
    public long? ResolvedAtUtcTicks { get; set; }
    public int Version { get; set; }
}
