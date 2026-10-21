namespace CivicLens.Application.Reports;

public sealed record ResolveReaderReportRequest(Guid ReportId, int ExpectedVersion, ReaderReportStatus Status, string? Note);
