using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Application.Analysis;

public static class DocumentChangeAnalysisLifecycle
{
    public static bool CanTransition(DocumentChangeAnalysisRecord record, DocumentChangeAnalysisCheckpoint update)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(update);
        if (!Matches(record, update) || !HasValidOutcome(record, update)) return false;
        return (record.Status, update.Status) switch
        {
            (State.Pending or State.RetryWaiting or State.WaitingForBudget, State.Running) => true,
            (State.WaitingForBudget or State.Failed, State.Pending) => true,
            (State.Running, _) => IsClaimed(record, update),
            _ => false
        };
    }

    private static bool Matches(DocumentChangeAnalysisRecord record, DocumentChangeAnalysisCheckpoint update) =>
        record.ComparisonId == update.ComparisonId && record.Task == update.Task && record.Status == update.ExpectedStatus &&
        record.LeaseToken == update.LeaseToken && record.Fence == update.Fence;

    private static bool IsClaimed(DocumentChangeAnalysisRecord record, DocumentChangeAnalysisCheckpoint update) =>
        record.Status == State.Running && !string.IsNullOrWhiteSpace(record.LeaseToken) && record.Fence > 0 &&
        update.ExpectedStatus == State.Running;

    private static bool HasValidOutcome(DocumentChangeAnalysisRecord record, DocumentChangeAnalysisCheckpoint update)
    {
        if (update.ErrorCode is not null && (string.IsNullOrWhiteSpace(update.ErrorCode) || update.ErrorCode.Length > 128))
            return false;
        if (update.RetryDelay is not null && update.Status != State.RetryWaiting) return false;
        if (update.Revision is not null &&
            (update.Status != State.Succeeded || update.ErrorCode is not null || update.Revision.ComparisonId != record.ComparisonId))
            return false;
        return update.Status switch
        {
            State.Pending or State.Running => update.ErrorCode is null,
            State.RetryWaiting => update.ErrorCode is not null && update.RetryDelay is { } delay &&
                delay >= TimeSpan.Zero && delay <= DocumentChangeAnalysisPolicy.MaximumRetryDelay,
            State.WaitingForBudget or State.Blocked or State.Failed => update.ErrorCode is not null,
            State.Succeeded => update.Revision is not null || update.ErrorCode is DocumentChangeAnalysisErrorCodes.DraftDiscarded or DocumentChangeAnalysisErrorCodes.HumanDraftExists,
            _ => false
        };
    }
}
