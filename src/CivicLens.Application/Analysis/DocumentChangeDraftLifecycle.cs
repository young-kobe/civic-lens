namespace CivicLens.Application.Analysis;

public static class DocumentChangeDraftLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is DocumentChangeDraftCheckpoint { Revision: not null } ||
        update.ErrorCode is DocumentChangeDraftErrorCodes.DraftDiscarded or DocumentChangeDraftErrorCodes.HumanDraftExists;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not DocumentChangeDraftCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.ComparisonId == record.SubjectId;
}
