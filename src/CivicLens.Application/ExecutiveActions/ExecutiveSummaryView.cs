using CivicLens.Application.Analysis;

namespace CivicLens.Application.ExecutiveActions;

public enum ExecutiveSummaryView { Pending, Drafted, Unchanged, RecordRejected, Failed }

public sealed record ExecutiveSummaryListItem(string RecordId, string Document, AnalysisState Status, ExecutiveSummaryView View, string? Code,
    int Attempts, string? LatestRunId);
