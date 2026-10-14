using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

public sealed record WatchedSourceConfiguration
{
    public const int DefaultCheckIntervalSeconds = 3600;
    public const int MinimumCheckIntervalSeconds = 60;
    public const int MaximumCheckIntervalSeconds = 604800;
    public const int MaximumInitialSpreadSeconds = 3600;

    public int ResolveCheckIntervalSeconds() => CheckIntervalSeconds ?? DefaultCheckIntervalSeconds;

    public TimeSpan InitialDueOffset()
    {
        var window = (ulong)Math.Min(ResolveCheckIntervalSeconds(), MaximumInitialSpreadSeconds);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Id));
        return TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt64BigEndian(hash) % window);
    }

    public required string Id { get; init; }
    public SourceCoverageConfiguration[]? Coverage { get; init; }
    public string? DocumentProfileId { get; init; }
    public required string Url { get; init; }
    public required string AllowedOrigin { get; init; }
    public required string AllowedPathPrefix { get; init; }
    public CollectionMode Mode { get; init; } = CollectionMode.Page;
    public string? Credential { get; init; }
    public ApiParser? ApiParser { get; init; }
    public int? MinimumCongress { get; init; }
    public DateOnly? BackfillFrom { get; init; }
    public int MaxCandidates { get; init; } = 100;
    public DiscoveryAdmissionPolicy? AdmissionPolicy { get; init; }
    public bool Enabled { get; init; } = true;
    public int MaxRequests { get; init; } = 5;
    public long MaxBytes { get; init; } = 2_000_000;
    public int TimeoutSeconds { get; init; } = 30;
    public int MinDelayMilliseconds { get; init; } = 1000;
    public int? CheckIntervalSeconds { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public CollectionJobPolicy? JobPolicy { get; init; }
}
