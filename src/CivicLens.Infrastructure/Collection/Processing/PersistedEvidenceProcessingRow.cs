using CivicLens.Application.Collection.Processing;

namespace CivicLens.Infrastructure.Collection.Processing;

internal sealed class PersistedEvidenceProcessingRow
{
    public string JobId { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string RequestedUrl { get; set; } = "";
    public long? ObservedAtUtcTicks { get; set; }
    public EvidenceProcessingStage Stage { get; set; }
    public EvidenceProcessingStatus Status { get; set; }
    public string? LeaseToken { get; set; }
    public long Fence { get; set; }
    public long? LeaseExpiresAt { get; set; }
    public int Attempts { get; set; }
    public long? RetryAt { get; set; }
    public string? PredecessorAttemptId { get; set; }
    public string? ExtractionId { get; set; }
    public string? ComparisonId { get; set; }
    public EvidenceProcessingOutcome? Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public int AdmittedCount { get; set; }
    public int DeferredCount { get; set; }
    public int DuplicateCount { get; set; }
}
