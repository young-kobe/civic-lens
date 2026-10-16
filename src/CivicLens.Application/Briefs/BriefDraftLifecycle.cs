using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public static class BriefDraftLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess, _ => true, _ => true);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is BriefDraftCheckpoint { Revision: not null } ||
        update.ErrorCode is BriefDraftErrorCodes.Unchanged or BriefDraftErrorCodes.NoNewRecords;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not BriefDraftCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.RecordId == record.SubjectId;
}
