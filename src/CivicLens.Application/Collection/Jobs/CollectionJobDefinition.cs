using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobDefinition
{
    public required string SourceId { get; init; }
    public required string Url { get; init; }
    public required string AllowedOrigin { get; init; }
    public required string AllowedPathPrefix { get; init; }
    public required int MaxRequests { get; init; }
    public required long MaxBytes { get; init; }
    public required int TimeoutSeconds { get; init; }
    public required int MinDelayMilliseconds { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public required CollectionJobPolicy Policy { get; init; }

    public static CollectionJobDefinition FromConfiguration(CollectionConfiguration configuration, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        var source = configuration.Sources.SingleOrDefault(item => item.Id == sourceId)
            ?? throw new ArgumentException($"Unknown source ID '{sourceId}'.", nameof(sourceId));
        if (!source.Enabled)
            throw new ArgumentException($"Source '{sourceId}' is disabled.", nameof(sourceId));
        var definition = new CollectionJobDefinition
        {
            SourceId = source.Id,
            Url = source.Url,
            AllowedOrigin = source.AllowedOrigin,
            AllowedPathPrefix = source.AllowedPathPrefix,
            MaxRequests = source.MaxRequests,
            MaxBytes = source.MaxBytes,
            TimeoutSeconds = source.TimeoutSeconds,
            MinDelayMilliseconds = source.MinDelayMilliseconds,
            ETag = source.ETag,
            LastModified = source.LastModified,
            Policy = source.JobPolicy ?? new CollectionJobPolicy()
        };
        definition.Validate();
        return definition;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceId) || string.IsNullOrWhiteSpace(Url) ||
            string.IsNullOrWhiteSpace(AllowedOrigin) || string.IsNullOrWhiteSpace(AllowedPathPrefix) || Policy is null)
            throw new ArgumentException("Job source snapshot is incomplete.");
        var request = CreateRequest("validation-job", Path.GetFullPath("collection-artifacts"));
        Policy.Validate(request);
    }

    public CollectionRequest CreateRequest(string jobId, string artifactDirectory) => new()
    {
        JobId = jobId,
        SourceId = SourceId,
        Url = Url,
        AllowedOrigin = AllowedOrigin,
        AllowedPathPrefix = AllowedPathPrefix,
        ArtifactDirectory = Path.GetFullPath(artifactDirectory),
        MaxRequests = MaxRequests,
        MaxBytes = MaxBytes,
        TimeoutSeconds = TimeoutSeconds,
        MinDelayMilliseconds = MinDelayMilliseconds,
        ETag = ETag,
        LastModified = LastModified
    };
}
