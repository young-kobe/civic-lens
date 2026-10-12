using CivicLens.Application.Analysis;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Application.Statements;

public static class StatementAnalysisLifecycle
{
    public static bool CanTransition(StatementAnalysisRecord record, StatementAnalysisCheckpoint update)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(update);
        if (!Matches(record, update) || !HasValidOutcome(update)) return false;
        return (record.Status, update.Status) switch
        {
            (State.Pending or State.RetryWaiting or State.WaitingForBudget, State.Running) => true,
            (State.WaitingForBudget or State.Failed, State.Pending) => true,
            (State.Succeeded, State.Pending) => record.ErrorCode == StatementGateReasons.NoTrackedIssue,
            (State.Running, _) => IsClaimed(record, update),
            _ => false
        };
    }

    private static bool Matches(StatementAnalysisRecord record, StatementAnalysisCheckpoint update) =>
        record.CandidateId == update.CandidateId && record.Task == update.Task && record.Status == update.ExpectedStatus &&
        record.LeaseToken == update.LeaseToken && record.Fence == update.Fence;

    private static bool IsClaimed(StatementAnalysisRecord record, StatementAnalysisCheckpoint update) =>
        record.Status == State.Running && !string.IsNullOrWhiteSpace(record.LeaseToken) && record.Fence > 0 &&
        update.ExpectedStatus == State.Running;

    private static bool HasValidOutcome(StatementAnalysisCheckpoint update)
    {
        if (update.ErrorCode is not null && (string.IsNullOrWhiteSpace(update.ErrorCode) || update.ErrorCode.Length > 128))
            return false;
        if (update.RetryDelay is not null && update.Status != State.RetryWaiting) return false;
        return update.Status switch
        {
            State.Pending or State.Running => update.ErrorCode is null,
            State.RetryWaiting => update.ErrorCode is not null && update.RetryDelay is { } delay &&
                delay >= TimeSpan.Zero && delay <= DocumentChangeAnalysisPolicy.MaximumRetryDelay,
            State.WaitingForBudget or State.Blocked or State.Failed => update.ErrorCode is not null,
            State.Succeeded => StatementGateReasons.IsKnown(update.ErrorCode),
            _ => false
        };
    }
}
