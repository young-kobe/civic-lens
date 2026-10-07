using CivicLens.Collection.Contracts;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobDefinition
{
    public CollectionMode Mode { get; init; } = CollectionMode.Page;
    public int MaxCandidates { get; init; } = 100;
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CollectionConfigurationRevision? ConfigurationRevision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? CoverageAsOf { get; init; }

    public static CollectionJobDefinition FromConfiguration(CollectionConfiguration configuration, string sourceId,
        DateOnly? asOf = null)
    {
        var revision = CollectionConfigurationRevision.Create(configuration);
        var snapshot = revision.ReadConfiguration();
        var effectiveDate = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        snapshot.CreateRequest(sourceId, "validation-job", Path.GetFullPath("collection-artifacts"), effectiveDate);
        var definition = FromSource(snapshot.Sources.Single(source => source.Id == sourceId)) with
        {
            ConfigurationRevision = revision,
            CoverageAsOf = effectiveDate
        };
        definition.Validate();
        return definition;
    }

    internal static CollectionJobDefinition FromSource(WatchedSourceConfiguration source)
    {
        var definition = new CollectionJobDefinition
        {
            SourceId = source.Id,
            Mode = source.Mode,
            MaxCandidates = source.MaxCandidates,
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
        return definition;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceId) || string.IsNullOrWhiteSpace(Url) ||
            string.IsNullOrWhiteSpace(AllowedOrigin) || string.IsNullOrWhiteSpace(AllowedPathPrefix) || Policy is null)
            throw new ArgumentException("Job source snapshot is incomplete.");
        var request = CreateRequest("validation-job", Path.GetFullPath("collection-artifacts"));
        Policy.Validate(request);
        ValidateConfigurationBinding();
    }

    private void ValidateConfigurationBinding()
    {
        if (ConfigurationRevision is null && CoverageAsOf is null)
            return; // Legacy or explicitly supplied standalone definitions have no registry provenance.
        if (ConfigurationRevision is null || CoverageAsOf is null)
            throw new ArgumentException("A configuration revision and coverage date must be supplied together.");

        var configuration = ConfigurationRevision.ReadConfiguration();
        configuration.CreateRequest(SourceId, "validation-job", Path.GetFullPath("collection-artifacts"), CoverageAsOf);
        var expected = FromSource(configuration.Sources.Single(source => source.Id == SourceId));
        if (expected.Mode is CollectionMode.Feed or CollectionMode.Html && Mode == CollectionMode.Page)
            expected = expected with { Mode = CollectionMode.Page, Url = Url, ETag = null, LastModified = null };
        if (expected != this with { ConfigurationRevision = null, CoverageAsOf = null })
            throw new ArgumentException("Job settings do not match the bound configuration revision.");
    }

    public bool MatchesReplayOf(CollectionJobDefinition existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        // Old jobs cannot gain historical provenance from a later configuration.
        return existing.ConfigurationRevision is null && existing.CoverageAsOf is null
            ? existing == this with { ConfigurationRevision = null, CoverageAsOf = null }
            : existing == this;
    }

    public CollectionRequest CreateRequest(string jobId, string artifactDirectory) => new()
    {
        JobId = jobId,
        SourceId = SourceId,
        Mode = Mode,
        MaxCandidates = MaxCandidates,
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
