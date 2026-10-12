using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementAnalysisCheckpoint(
    string CandidateId, string Task, DocumentChangeAnalysisState ExpectedStatus, string? LeaseToken, long Fence,
    DocumentChangeAnalysisState Status, string? ErrorCode = null, TimeSpan? RetryDelay = null)
{
    public static StatementAnalysisCheckpoint From(StatementAnalysisClaim claim, DocumentChangeAnalysisState status,
        string? errorCode = null, TimeSpan? retryDelay = null) =>
        new(claim.Record.CandidateId, claim.Record.Task, DocumentChangeAnalysisState.Running, claim.LeaseToken, claim.Fence,
            status, errorCode, retryDelay);
}
