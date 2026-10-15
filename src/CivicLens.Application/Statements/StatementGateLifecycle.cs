using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementGateLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(update => StatementGateReasons.IsKnown(update.ErrorCode),
        record => record.ErrorCode == StatementGateReasons.NoTrackedIssue);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) => Rules.CanTransition(record, update);
}
