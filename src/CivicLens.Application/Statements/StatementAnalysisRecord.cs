using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public sealed record StatementAnalysisRecord(
    string CandidateId,
    string Task,
    DocumentChangeAnalysisState Status,
    string? LeaseToken,
    long Fence,
    int Attempts,
    DateTimeOffset? RetryAt,
    string? ErrorCode,
    string? RetryOfRunId = null);
