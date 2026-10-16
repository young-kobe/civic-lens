using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public static class VoteDraftLifecycle
{
    private static readonly AnalysisLifecycle Rules = new(AcceptsSuccess);

    public static bool CanTransition(AnalysisRecord record, AnalysisCheckpoint update) =>
        Rules.CanTransition(record, update) && HasConsistentRevision(record, update);

    private static bool AcceptsSuccess(AnalysisCheckpoint update) =>
        update is VoteDraftCheckpoint { Revision: not null } ||
        update.ErrorCode is VoteDraftErrorCodes.Unchanged or VoteDraftErrorCodes.SupersededByNewerCandidate or
            VoteDraftErrorCodes.NoTrackedMember;

    private static bool HasConsistentRevision(AnalysisRecord record, AnalysisCheckpoint update) =>
        update is not VoteDraftCheckpoint { Revision: { } revision } ||
        update.Status == AnalysisState.Succeeded && update.ErrorCode is null && revision.CandidateId == record.SubjectId;
}
