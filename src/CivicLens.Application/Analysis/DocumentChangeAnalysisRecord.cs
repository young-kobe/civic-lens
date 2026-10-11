namespace CivicLens.Application.Analysis;

public enum DocumentChangeAnalysisState { Pending, Running, RetryWaiting, WaitingForBudget, Succeeded, Blocked, Failed }

public sealed record DocumentChangeAnalysisRecord(
    string ComparisonId,
    string Task,
    DocumentChangeAnalysisState Status,
    string? LeaseToken,
    long Fence,
    int Attempts,
    DateTimeOffset? RetryAt,
    string? DraftId,
    string? ErrorCode,
    string? RetryOfRunId = null);
