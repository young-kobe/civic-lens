using System.Collections.Immutable;

namespace CivicLens.Application.Reports;

public sealed record ReaderReportPage(ImmutableArray<ReaderReportListItem> Items, string? NewerCursor, string? OlderCursor);
