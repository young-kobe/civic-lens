using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    ActionDraftRevision? Revision = null, int ReadStateVersion = 0)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static ActionDraftCheckpoint From(AnalysisClaim claim, ActionDraftRevision revision, int readStateVersion = 0) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, AnalysisState.Succeeded,
            Revision: revision, ReadStateVersion: readStateVersion);
}
