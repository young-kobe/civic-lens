namespace CivicLens.Infrastructure.Collection.Persistence;

internal sealed class AttemptRow
{
    public string AttemptId { get; set; } = null!;
    public string SourceId { get; set; } = null!;
    public string RequestedUrl { get; set; } = null!;
    public string FinalUrl { get; set; } = null!;
    public long ObservedAtUtcTicks { get; set; }
    public string Outcome { get; set; } = null!;
    public bool HasResponse { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseETag { get; set; }
    public long? ResponseLastModifiedUtcTicks { get; set; }
    public string? ResponseContentType { get; set; }
    public string[]? ResponseContentEncodings { get; set; }
    public bool HasSentValidators { get; set; }
    public string? SentETag { get; set; }
    public long? SentLastModifiedUtcTicks { get; set; }
    public long? RobotsCrawlDelayMilliseconds { get; set; }
    public string? FailureCode { get; set; }
    public long? RetryDelayTicks { get; set; }
    public string? CaptureSha256 { get; set; }
    public string? PriorCaptureAttemptId { get; set; }
    public string? PriorCaptureSha256 { get; set; }
}
