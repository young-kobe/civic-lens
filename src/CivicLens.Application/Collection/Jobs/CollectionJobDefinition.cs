using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Collection.Contracts;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobDefinition
{
    public CollectionMode Mode { get; init; } = CollectionMode.Page;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Credential { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiResource? ApiResource { get; init; }
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

    public PacingGroupConfiguration? ResolvePacingGroup() =>
        ConfigurationRevision?.Configuration.PacingGroupOf(AllowedOrigin);

    private string? ConfiguredUrl =>
        ConfigurationRevision?.Configuration.Sources.FirstOrDefault(source => source.Id == SourceId)?.Url;

    public bool IsRoot() =>
        ApiResource is Collection.ApiResource.BillList or Collection.ApiResource.HouseVoteList || ConfiguredUrl == Url;

    public bool IsListingPage()
    {
        var path = PathOf(Url);
        return path == ComparablePath(AllowedPathPrefix) || ConfiguredUrl is not null && path == PathOf(ConfiguredUrl);
    }

    private static string PathOf(string url) =>
        ComparablePath(Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.AbsolutePath : url);

    private static string ComparablePath(string path) => Uri.UnescapeDataString(path).TrimEnd('/');

    public static CollectionJobDefinition FromConfiguration(CollectionConfiguration configuration, string sourceId,
        DateOnly? asOf = null)
    {
        var revision = CollectionConfigurationRevision.Create(configuration);
        var effectiveDate = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var definition = FromSource(revision.Configuration.EligibleSource(sourceId, effectiveDate)) with
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
            Credential = source.Credential,
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
        if (ConfigurationRevision is null && CoverageAsOf is null && ApiResource is null)
            return; // Legacy or explicitly supplied standalone definitions have no registry provenance.
        if (ConfigurationRevision is null || CoverageAsOf is null)
            throw new ArgumentException("A configuration revision and coverage date must be supplied together.");

        var source = ConfigurationRevision.Configuration.EligibleSource(SourceId, CoverageAsOf.Value);
        var expected = FromSource(source);
        if (ApiResource is { } resource)
        {
            if (!MatchesResource(resource, source, Url))
                throw new ArgumentException("Job URL does not have the form of its API resource.");
            expected = expected with { ApiResource = resource, Url = Url };
        }
        if (expected.Mode is CollectionMode.Feed or CollectionMode.Html && Mode == CollectionMode.Page)
            expected = expected with { Mode = CollectionMode.Page, Url = Url, ETag = null, LastModified = null };
        if (expected != this with { ConfigurationRevision = null, CoverageAsOf = null })
            throw new ArgumentException("Job settings do not match the bound configuration revision.");
    }

    private static bool MatchesResource(ApiResource resource, WatchedSourceConfiguration source, string url) =>
        source.ApiParser switch
        {
            ApiParser.CongressBillList => MatchesBillResource(resource, source.Url, url),
            ApiParser.CongressHouseVoteList => MatchesHouseVoteResource(resource, source.Url, url),
            ApiParser.CongressLegislators => resource == Collection.ApiResource.MemberRoster && url == source.Url,
            _ => false
        };

    private static bool MatchesBillResource(ApiResource resource, string sourceUrl, string url) =>
        resource switch
        {
            Collection.ApiResource.BillList => BillStreamWindow.IsRequestUrl(sourceUrl, url),
            Collection.ApiResource.Bill => CongressBillListItem.IsDetailUrl(sourceUrl, url),
            Collection.ApiResource.BillCosponsors or Collection.ApiResource.BillActions or
            Collection.ApiResource.BillSummaries =>
                BillPageUrl.TryRead(sourceUrl, resource, url) is not null,
            _ => false
        };

    private static bool MatchesHouseVoteResource(ApiResource resource, string sourceUrl, string url) =>
        resource switch
        {
            Collection.ApiResource.HouseVoteList => HouseVoteListUrl.TryRead(sourceUrl, url) is not null,
            Collection.ApiResource.HouseVoteDetail => HouseVoteRollUrl.TryReadDetail(sourceUrl, url) is not null,
            Collection.ApiResource.HouseVoteMembers => HouseVoteRollUrl.TryReadMembers(sourceUrl, url) is not null,
            _ => false
        };

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
        Credential = Credential,
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
