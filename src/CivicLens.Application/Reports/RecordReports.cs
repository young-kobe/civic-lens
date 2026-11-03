namespace CivicLens.Application.Reports;

/// <summary>The oldest reports of a record, the number of open reports beyond them, and the current revision of the record.</summary>
public sealed record RecordReports(IReadOnlyList<ReaderReport> Reports, int MoreOpen, int? CurrentRevisionNumber);
