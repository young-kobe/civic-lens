namespace CivicLens.Application.Analysis;

public record AnalysisCheckpoint(
    string SubjectId, string Task, AnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    AnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null)
{
    public static AnalysisCheckpoint From(AnalysisClaim claim, AnalysisState status, string? errorCode = null,
        TimeSpan? retryDelay = null) =>
        new(claim.Record.SubjectId, claim.Record.Task, AnalysisState.Running, claim.LeaseToken, claim.Fence, status, errorCode,
            retryDelay);
}
