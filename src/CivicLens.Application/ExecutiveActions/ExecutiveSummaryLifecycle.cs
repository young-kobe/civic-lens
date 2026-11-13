using CivicLens.Application.Analysis;

namespace CivicLens.Application.ExecutiveActions;

public static class ExecutiveSummaryLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess, record => AnalysisLifecycle.IsDraftedOrUnchanged(record, ExecutiveSummaryErrorCodes.Unchanged), _ => true);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is ExecutiveSummaryCheckpoint { Revision: not null } ||
        update.ErrorCode is ExecutiveSummaryErrorCodes.Unchanged or ExecutiveSummaryErrorCodes.RecordRejected;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not ExecutiveSummaryCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.RecordId == record.SubjectId;
}
