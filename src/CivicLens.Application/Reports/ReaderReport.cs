using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Reports;

/// <summary>A reader report in the operational queue. It holds nothing about the reporter.</summary>
public sealed record ReaderReport(Guid ReportId, string RecordId, PublishedRecordKind Kind, int RevisionNumber, string Category,
    string Text, DateTimeOffset ReceivedAtUtc, ReaderReportStatus Status, string? Note, string? ResolvedBy, DateTimeOffset? ResolvedAtUtc,
    int Version);
