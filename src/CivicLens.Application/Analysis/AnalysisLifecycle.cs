namespace CivicLens.Application.Analysis;

public sealed class AnalysisLifecycle(Func<AnalysisCheckpoint, bool> acceptsSuccess,
    Func<AnalysisRecord, bool>? allowsRequeueOfSucceeded = null, Func<AnalysisRecord, bool>? allowsRequeueOfBlocked = null)
{
    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(update);
        if (!Matches(record, update) || !HasValidOutcome(update)) return false;
        return (record.Status, update.Status) switch
        {
            (AnalysisState.Pending or AnalysisState.RetryWaiting or AnalysisState.WaitingForBudget, AnalysisState.Running) => true,
            (AnalysisState.WaitingForBudget or AnalysisState.Failed, AnalysisState.Pending) => true,
            (AnalysisState.Succeeded, AnalysisState.Pending) => allowsRequeueOfSucceeded?.Invoke(record) == true,
            (AnalysisState.Blocked, AnalysisState.Pending) => allowsRequeueOfBlocked?.Invoke(record) == true,
            (AnalysisState.Running, _) => IsClaimed(record, update),
            _ => false
        };
    }

    private static bool Matches(AnalysisRecord record, AnalysisCheckpoint update) =>
        record.SubjectId == update.SubjectId && record.Task == update.Task && record.Status == update.ExpectedStatus &&
        record.LeaseToken == update.LeaseToken && record.Fence == update.Fence;

    private static bool IsClaimed(AnalysisRecord record, AnalysisCheckpoint update) =>
        record.Status == AnalysisState.Running && !string.IsNullOrWhiteSpace(record.LeaseToken) && record.Fence > 0 &&
        update.ExpectedStatus == AnalysisState.Running;

    private bool HasValidOutcome(AnalysisCheckpoint update)
    {
        if (update.ErrorCode is not null && (string.IsNullOrWhiteSpace(update.ErrorCode) || update.ErrorCode.Length > 128))
            return false;
        if (update.RetryDelay is not null && update.Status != AnalysisState.RetryWaiting) return false;
        return update.Status switch
        {
            AnalysisState.Pending or AnalysisState.Running => update.ErrorCode is null,
            AnalysisState.RetryWaiting => update.ErrorCode is not null && update.RetryDelay is { } delay &&
                delay >= TimeSpan.Zero && delay <= AnalysisPolicy.MaximumRetryDelay,
            AnalysisState.WaitingForBudget or AnalysisState.Blocked or AnalysisState.Failed => update.ErrorCode is not null,
            AnalysisState.Succeeded => acceptsSuccess(update),
            _ => false
        };
    }
}
