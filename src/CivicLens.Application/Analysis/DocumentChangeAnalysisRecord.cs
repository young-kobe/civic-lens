namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisRecord(
    string SubjectId,
    string Task,
    AnalysisState Status,
    string? LeaseToken,
    long Fence,
    int Attempts,
    DateTimeOffset? RetryAt,
    string? DraftId,
    string? ErrorCode,
    string? RetryOfRunId = null)
    : AnalysisRecord(SubjectId, Task, Status, LeaseToken, Fence, Attempts, RetryAt, ErrorCode, RetryOfRunId);
