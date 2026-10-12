namespace CivicLens.Application.Analysis;

public record AnalysisRecord(
    string SubjectId,
    string Task,
    AnalysisState Status,
    string? LeaseToken,
    long Fence,
    int Attempts,
    DateTimeOffset? RetryAt,
    string? ErrorCode,
    string? RetryOfRunId = null);
