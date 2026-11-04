using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobAttemptRow
{
    public string AttemptId { get; set; } = "";
    public string JobId { get; set; } = "";
    public int Sequence { get; set; }
    public int RequestVersion { get; set; }
    public string RequestJobId { get; set; } = "";
    public string RequestSourceId { get; set; } = "";
    public string RequestUrl { get; set; } = "";
    public string RequestAllowedOrigin { get; set; } = "";
    public string RequestAllowedPathPrefix { get; set; } = "";
    public string RequestArtifactDirectory { get; set; } = "";
    public CollectionMode RequestMode { get; set; }
    public int RequestMaxCandidates { get; set; }
    public int RequestMaxRequests { get; set; }
    public long RequestMaxBytes { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public int RequestMinDelayMilliseconds { get; set; }
    public string? RequestETag { get; set; }
    public long? RequestLastModifiedUtcTicks { get; set; }
    public string? RequestCredential { get; set; }
    public long StartedAt { get; set; }
    public CollectionJobAttemptOutcome? Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public long? RetryDelayTicks { get; set; }
    public long? RobotsCrawlDelayMilliseconds { get; set; }
    public long? CompletedAt { get; set; }
}
