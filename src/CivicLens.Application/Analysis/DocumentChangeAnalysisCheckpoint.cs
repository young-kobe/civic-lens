using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisCheckpoint(
    string ComparisonId, string Task, DocumentChangeAnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    DocumentChangeAnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    DocumentChangeDraftRevision? Revision = null)
{
    public static DocumentChangeAnalysisCheckpoint From(DocumentChangeAnalysisClaim claim, DocumentChangeAnalysisState status,
        string? errorCode = null, TimeSpan? retryDelay = null, DocumentChangeDraftRevision? revision = null) =>
        new(claim.Record.ComparisonId, claim.Record.Task, DocumentChangeAnalysisState.Running, claim.LeaseToken, claim.Fence,
            status, errorCode, retryDelay, revision);
}
