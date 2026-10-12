using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    DocumentChangeDraftRevision? Revision = null)
    : AnalysisCheckpoint(SubjectId, Task, ExpectedStatus, LeaseToken, Fence, Status, ErrorCode, RetryDelay)
{
    public static DocumentChangeAnalysisCheckpoint From(AnalysisClaim claim, AnalysisState status, string? errorCode = null,
        TimeSpan? retryDelay = null, DocumentChangeDraftRevision? revision = null) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, status, errorCode,
            retryDelay, revision);
}
