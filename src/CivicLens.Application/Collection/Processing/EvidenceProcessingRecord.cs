namespace CivicLens.Application.Collection.Processing;

public enum EvidenceProcessingStage { Preparation, Extraction, Comparison, Complete }
public enum EvidenceProcessingStatus { Pending, Running, RetryWaiting, WaitingForPredecessor, Succeeded, Blocked, Failed }
public enum EvidenceProcessingOutcome { Prepared, Baseline, Unchanged, Changed, Blocked, Failed }

/// <summary>Durable progress for one captured page, separate from collection job state.</summary>
public sealed record EvidenceProcessingRecord(
    string JobId,
    string AttemptId,
    string SourceId,
    string RequestedUrl,
    EvidenceProcessingStage Stage,
    EvidenceProcessingStatus Status,
    string? LeaseToken,
    long Fence,
    int Attempts,
    DateTimeOffset? RetryAt,
    string? ExtractionId,
    string? ComparisonId,
    EvidenceProcessingOutcome? Outcome,
    string? ErrorCode,
    int AdmittedCount = 0,
    int DeferredCount = 0,
    int DuplicateCount = 0,
    string? PredecessorAttemptId = null);
