using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public static class StatementDraftLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is StatementDraftCheckpoint { Revision: not null } ||
        update.ErrorCode is StatementDraftErrorCodes.OverByteCap or StatementDraftErrorCodes.DraftDiscarded or StatementDraftErrorCodes.HumanDraftExists;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not StatementDraftCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.CandidateId == record.SubjectId;
}
