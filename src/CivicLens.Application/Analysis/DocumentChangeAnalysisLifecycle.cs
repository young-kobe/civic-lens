namespace CivicLens.Application.Analysis;

public static class DocumentChangeAnalysisLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is DocumentChangeAnalysisCheckpoint { Revision: not null } ||
        update.ErrorCode is DocumentChangeAnalysisErrorCodes.DraftDiscarded or DocumentChangeAnalysisErrorCodes.HumanDraftExists;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not DocumentChangeAnalysisCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.ComparisonId == record.SubjectId;
}
