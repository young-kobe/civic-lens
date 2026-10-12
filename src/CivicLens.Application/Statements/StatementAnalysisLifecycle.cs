using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementAnalysisLifecycle
{
    public static readonly AnalysisLifecycle Rules = new(update => StatementGateReasons.IsKnown(update.ErrorCode),
        record => record.ErrorCode == StatementGateReasons.NoTrackedIssue);
}
