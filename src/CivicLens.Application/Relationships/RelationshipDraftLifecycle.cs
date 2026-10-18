using CivicLens.Application.Analysis;

namespace CivicLens.Application.Relationships;

public static class RelationshipDraftLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess, _ => true, _ => true);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is RelationshipDraftCheckpoint { Revision: not null } ||
        update.ErrorCode is RelationshipDraftErrorCodes.Unchanged or RelationshipDraftErrorCodes.NoRelationship or
            RelationshipDraftErrorCodes.NoLongerMatches;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not RelationshipDraftCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.RecordId == record.SubjectId;
}
