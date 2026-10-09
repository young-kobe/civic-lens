namespace CivicLens.Application.Collection.Processing;

public sealed record EvidenceProcessingCheckpoint(
    string JobId, string AttemptId, EvidenceProcessingStage ExpectedStage,
    EvidenceProcessingStatus ExpectedStatus, string LeaseToken, long Fence,
    EvidenceProcessingStage Stage, EvidenceProcessingStatus Status,
    string? ExtractionId = null, string? ComparisonId = null,
    EvidenceProcessingOutcome? Outcome = null, string? ErrorCode = null, TimeSpan? RetryDelay = null,
    int AdmittedCount = 0, int DeferredCount = 0, int DuplicateCount = 0);
