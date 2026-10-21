namespace CivicLens.Application.Reports;

/// <summary>A report with the current review revision of its record. The revision is null when review storage has no such record.</summary>
public sealed record ReaderReportListItem(ReaderReport Report, int? CurrentRevisionNumber);
