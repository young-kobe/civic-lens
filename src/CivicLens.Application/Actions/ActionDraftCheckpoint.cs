using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

public sealed record ActionDraftCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    ActionDraftRevision? Revision = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static ActionDraftCheckpoint From(AnalysisClaim claim, AnalysisState status, string? errorCode = null,
        TimeSpan? retryDelay = null, ActionDraftRevision? revision = null) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, status, errorCode,
            retryDelay, revision);
}
