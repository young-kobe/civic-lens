using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public sealed record WatchedSourceConfiguration
{
    public required string Id { get; init; }
    public string[]? PersonIds { get; init; }
    public SourceCoverageConfiguration[]? Coverage { get; init; }
    public string? DocumentProfileId { get; init; }
    public required string Url { get; init; }
    public required string AllowedOrigin { get; init; }
    public required string AllowedPathPrefix { get; init; }
    public CollectionMode Mode { get; init; } = CollectionMode.Page;
    public int MaxCandidates { get; init; } = 100;
    public DiscoveryAdmissionPolicy? AdmissionPolicy { get; init; }
    public bool Enabled { get; init; } = true;
    public int MaxRequests { get; init; } = 5;
    public long MaxBytes { get; init; } = 2_000_000;
    public int TimeoutSeconds { get; init; } = 30;
    public int MinDelayMilliseconds { get; init; } = 1000;
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public CollectionJobPolicy? JobPolicy { get; init; }
}
