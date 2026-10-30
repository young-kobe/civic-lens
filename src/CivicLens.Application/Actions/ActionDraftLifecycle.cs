using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public static class ActionDraftLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess, record => AnalysisLifecycle.IsDraftedOrUnchanged(record, ActionDraftErrorCodes.Unchanged), _ => true);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is ActionDraftCheckpoint { Revision: not null } ||
        update.ErrorCode is ActionDraftErrorCodes.Unchanged or ActionDraftErrorCodes.SupersededByNewerCandidate or
            ActionDraftErrorCodes.NoTrackedOfficial;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not ActionDraftCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.CandidateId == record.SubjectId;
}
