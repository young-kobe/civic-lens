using CivicLens.Collection.Contracts;
using System.Text.Json.Serialization;

namespace CivicLens.Application;

public sealed record CollectionConfiguration
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public required PersonConfiguration[] People { get; init; }
    public required WatchedSourceConfiguration[] Sources { get; init; }

    public void Validate()
    {
        if (Version != 1)
            throw new ArgumentException("Unsupported collection configuration version.");
        if (People is null || Sources is null)
            throw new ArgumentException("People and sources are required.");

        var peopleById = new Dictionary<string, PersonConfiguration>(StringComparer.Ordinal);
        foreach (var person in People)
        {
            if (person is null || string.IsNullOrWhiteSpace(person.Id) || string.IsNullOrWhiteSpace(person.Name))
                throw new ArgumentException("Every person must have a nonblank ID and name.");
            if (!peopleById.TryAdd(person.Id, person))
                throw new ArgumentException($"Duplicate person ID '{person.Id}'.");
        }

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var sourceUrls = new HashSet<Uri>();
        foreach (var source in Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id) || source.PersonIds is null)
                throw new ArgumentException("Every source must have an ID and person membership list.");
            if (!sourceIds.Add(source.Id))
                throw new ArgumentException($"Duplicate source ID '{source.Id}'.");

            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url))
                throw new ArgumentException($"Source '{source.Id}' must have an absolute URL.");
            if (!sourceUrls.Add(url))
                throw new ArgumentException("A source URL may be configured only once; add people to its membership list.");

            if (source.PersonIds.Length == 0 || source.PersonIds.Any(id => string.IsNullOrWhiteSpace(id) || !peopleById.ContainsKey(id)))
                throw new ArgumentException($"Source '{source.Id}' references a missing person.");
            if (source.PersonIds.Distinct(StringComparer.Ordinal).Count() != source.PersonIds.Length)
                throw new ArgumentException($"Source '{source.Id}' has duplicate person memberships.");

            var request = BuildRequest(source, "validation-job", Path.GetFullPath("collection-artifacts"));
            request.Validate();
        }
    }

    public CollectionRequest CreateRequest(string sourceId, string jobId, string artifactDirectory)
    {
        Validate();
        if (Sources is null)
            throw new ArgumentException("Sources are required.");
        var source = Sources.FirstOrDefault(candidate => candidate is not null && candidate.Id == sourceId)
            ?? throw new ArgumentException($"Unknown source ID '{sourceId}'.", nameof(sourceId));
        if (!source.Enabled)
            throw new ArgumentException($"Source '{sourceId}' is disabled.", nameof(sourceId));

        var request = BuildRequest(source, jobId, artifactDirectory);
        request.Validate();
        return request;
    }

    private static CollectionRequest BuildRequest(WatchedSourceConfiguration source, string jobId, string artifactDirectory)
    {
        var request = new CollectionRequest
        {
            JobId = jobId,
            SourceId = source.Id,
            Url = source.Url,
            AllowedOrigin = source.AllowedOrigin,
            AllowedPathPrefix = source.AllowedPathPrefix,
            ArtifactDirectory = artifactDirectory,
            MaxRequests = source.MaxRequests,
            MaxBytes = source.MaxBytes,
            TimeoutSeconds = source.TimeoutSeconds,
            MinDelayMilliseconds = source.MinDelayMilliseconds
        };
        return request;
    }
}

public sealed record PersonConfiguration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed record WatchedSourceConfiguration
{
    public required string Id { get; init; }
    public required string[] PersonIds { get; init; }
    public required string Url { get; init; }
    public required string AllowedOrigin { get; init; }
    public required string AllowedPathPrefix { get; init; }
    public bool Enabled { get; init; } = true;
    public int MaxRequests { get; init; } = 5;
    public long MaxBytes { get; init; } = 2_000_000;
    public int TimeoutSeconds { get; init; } = 30;
    public int MinDelayMilliseconds { get; init; } = 1000;
}
