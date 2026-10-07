using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Registry;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

public sealed record CollectionConfiguration
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public required PersonConfiguration[] People { get; init; }
    public required WatchedSourceConfiguration[] Sources { get; init; }
    public DocumentProfileConfiguration[]? DocumentProfiles { get; init; }

    public void Validate()
    {
        if (Version is not (1 or 2))
            throw new ArgumentException("Unsupported collection configuration version.");
        if (People is null || Sources is null)
            throw new ArgumentException("People and sources are required.");
        if (Version == 1 && (DocumentProfiles is not null || Sources.Any(source => source?.DocumentProfileId is not null)))
            throw new ArgumentException("Document profiles require collection configuration version 2.");

        var peopleById = new Dictionary<string, PersonConfiguration>(StringComparer.Ordinal);
        foreach (var person in People)
        {
            if (person is null || string.IsNullOrWhiteSpace(person.Id) || string.IsNullOrWhiteSpace(person.Name))
                throw new ArgumentException("Every person must have a nonblank ID and name.");
            if (Version == 1 && person.Names is not null)
                throw new ArgumentException("Dated names require collection configuration version 2.");
            ValidateNames(person);
            if (!peopleById.TryAdd(person.Id, person))
                throw new ArgumentException($"Duplicate person ID '{person.Id}'.");
        }

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var profilesById = new Dictionary<string, DocumentProfileConfiguration>(StringComparer.Ordinal);
        if (DocumentProfiles is not null)
        {
            foreach (var profile in DocumentProfiles)
            {
                if (profile is null)
                    throw new ArgumentException("Document profiles cannot contain null entries.");
                _ = profile.ToProfile();
                if (!profilesById.TryAdd(profile.Id, profile))
                    throw new ArgumentException($"Duplicate document profile ID '{profile.Id}'.");
            }
        }

        var sourceUrls = new HashSet<Uri>();
        foreach (var source in Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id))
                throw new ArgumentException("Every source must have an ID.");
            ValidateCoverageShape(source);
            if (!sourceIds.Add(source.Id))
                throw new ArgumentException($"Duplicate source ID '{source.Id}'.");
            if (source.DocumentProfileId is not null && !profilesById.ContainsKey(source.DocumentProfileId))
                throw new ArgumentException($"Source '{source.Id}' references a missing document profile.");

            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url))
                throw new ArgumentException($"Source '{source.Id}' must have an absolute URL.");
            if (!sourceUrls.Add(url))
                throw new ArgumentException("A source URL may be configured only once; add people to its membership list.");

            if (Version == 1)
                ValidateLegacyMembership(source, peopleById);
            else
                ValidateDatedCoverage(source, peopleById);

            var request = BuildRequest(source, "validation-job", Path.GetFullPath("collection-artifacts"));
            request.Validate();
            source.JobPolicy?.Validate(request);
            source.AdmissionPolicy?.Validate();
        }
    }

    public CollectionRequest CreateRequest(string sourceId, string jobId, string artifactDirectory, DateOnly? asOf = null)
    {
        Validate();
        if (Sources is null)
            throw new ArgumentException("Sources are required.");
        var source = Sources.FirstOrDefault(candidate => candidate is not null && candidate.Id == sourceId)
            ?? throw new ArgumentException($"Unknown source ID '{sourceId}'.", nameof(sourceId));
        if (!source.Enabled)
            throw new ArgumentException($"Source '{sourceId}' is disabled.", nameof(sourceId));
        var effectiveDate = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (Version == 2 && !source.Coverage!.Any(coverage => new DateRange(coverage.StartsOn, coverage.EndsBefore).Contains(effectiveDate)))
            throw new ArgumentException($"Source '{sourceId}' has no active person coverage on {effectiveDate:yyyy-MM-dd}.", nameof(sourceId));

        var request = BuildRequest(source, jobId, artifactDirectory);
        request.Validate();
        return request;
    }

    public string[] GetCoveredPersonIds(string sourceId, DateOnly asOf)
    {
        Validate();
        var source = Sources.FirstOrDefault(candidate => candidate is not null && candidate.Id == sourceId)
            ?? throw new ArgumentException($"Unknown source ID '{sourceId}'.", nameof(sourceId));
        IEnumerable<string> ids = Version == 1
            ? source.PersonIds!
            : source.Coverage!.Where(coverage => new DateRange(coverage.StartsOn, coverage.EndsBefore).Contains(asOf))
                .Select(coverage => coverage.PersonId);
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private void ValidateCoverageShape(WatchedSourceConfiguration source)
    {
        if (Version == 1 && (source.PersonIds is null || source.Coverage is not null))
            throw new ArgumentException("Version 1 sources require PersonIds and cannot define Coverage.");
        if (Version == 2 && (source.PersonIds is not null || source.Coverage is null))
            throw new ArgumentException("Version 2 sources require Coverage and cannot define PersonIds.");
    }

    private static void ValidateLegacyMembership(WatchedSourceConfiguration source,
        IReadOnlyDictionary<string, PersonConfiguration> peopleById)
    {
        var ids = source.PersonIds!;
        if (ids.Length == 0 || ids.Any(id => string.IsNullOrWhiteSpace(id) || !peopleById.ContainsKey(id)))
            throw new ArgumentException($"Source '{source.Id}' references a missing person.");
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException($"Source '{source.Id}' has duplicate person memberships.");
    }

    private static void ValidateDatedCoverage(WatchedSourceConfiguration source,
        IReadOnlyDictionary<string, PersonConfiguration> peopleById)
    {
        var coverage = source.Coverage!;
        if (coverage.Length == 0)
            throw new ArgumentException($"Source '{source.Id}' must define at least one coverage interval.");
        var intervalsByPerson = new Dictionary<string, List<DateRange>>(StringComparer.Ordinal);
        foreach (var item in coverage)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.PersonId) || !peopleById.ContainsKey(item.PersonId))
                throw new ArgumentException($"Source '{source.Id}' references a missing person.");
            var range = new DateRange(item.StartsOn, item.EndsBefore);
            if (!intervalsByPerson.TryGetValue(item.PersonId, out var ranges))
                intervalsByPerson.Add(item.PersonId, ranges = []);
            if (ranges.Any(existing => existing.Overlaps(range)))
                throw new ArgumentException($"Source '{source.Id}' has overlapping coverage intervals for person '{item.PersonId}'.");
            ranges.Add(range);
        }
    }

    private static void ValidateNames(PersonConfiguration person)
    {
        if (person.Names is null)
            return;
        var names = new Dictionary<string, List<DateRange>>(StringComparer.Ordinal);
        foreach (var item in person.Names)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException($"Person '{person.Id}' has an invalid dated name.");
            var name = new DatedName(item.Name, new DateRange(item.StartsOn, item.EndsBefore));
            var range = name.Range;
            if (!names.TryGetValue(name.Name, out var ranges))
                names.Add(item.Name, ranges = []);
            if (ranges.Any(existing => existing.Overlaps(range)))
                throw new ArgumentException($"Person '{person.Id}' has overlapping intervals for name '{item.Name}'.");
            ranges.Add(range);
        }
    }

    private static CollectionRequest BuildRequest(WatchedSourceConfiguration source, string jobId, string artifactDirectory)
    {
        var request = new CollectionRequest
        {
            JobId = jobId,
            SourceId = source.Id,
            Mode = source.Mode,
            MaxCandidates = source.MaxCandidates,
            Url = source.Url,
            AllowedOrigin = source.AllowedOrigin,
            AllowedPathPrefix = source.AllowedPathPrefix,
            ArtifactDirectory = artifactDirectory,
            MaxRequests = source.MaxRequests,
            MaxBytes = source.MaxBytes,
            TimeoutSeconds = source.TimeoutSeconds,
            MinDelayMilliseconds = source.MinDelayMilliseconds,
            ETag = source.ETag,
            LastModified = source.LastModified
        };
        return request;
    }
}
