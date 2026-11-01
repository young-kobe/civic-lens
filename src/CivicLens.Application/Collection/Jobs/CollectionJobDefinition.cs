using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Application.Collection.VoteviewVotes;
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
    public required int Priority { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CollectionConfigurationRevision? ConfigurationRevision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? CoverageAsOf { get; init; }

    public PacingGroupConfiguration? ResolvePacingGroup() =>
        ConfigurationRevision!.Configuration.PacingGroupOf(AllowedOrigin);

    private string? ConfiguredUrl =>
        ConfigurationRevision!.Configuration.Sources.FirstOrDefault(source => source.Id == SourceId)?.Url;

    public bool IsRoot() =>
        ApiResource is Collection.ApiResource.BillList or Collection.ApiResource.HouseVoteList or
            Collection.ApiResource.VoteviewRollCalls || ConfiguredUrl == Url;

    public bool IsListingPage()
    {
        var path = PathOf(Url);
        return path == ComparablePath(AllowedPathPrefix) || ConfiguredUrl is not null && path == PathOf(ConfiguredUrl);
    }

    private static string PathOf(string url) =>
        ComparablePath(Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.AbsolutePath : url);

    private static string ComparablePath(string path) => Uri.UnescapeDataString(path).TrimEnd('/');

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
            Policy = source.JobPolicy ?? new CollectionJobPolicy(),
            Priority = CollectionJobPriority.Check
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
        if (ConfigurationRevision is null || CoverageAsOf is null)
            throw new ArgumentException("A job definition needs a configuration revision and a coverage date.");
        if (!CollectionJobPriority.IsValid(Priority))
            throw new ArgumentException("Job priority is not a collection job priority.");

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
        if (expected != this with { ConfigurationRevision = null, CoverageAsOf = null, Priority = expected.Priority })
            throw new ArgumentException("Job settings do not match the bound configuration revision.");
    }

    private static bool MatchesResource(ApiResource resource, WatchedSourceConfiguration source, string url) =>
        source.ApiParser switch
        {
            ApiParser.CongressBillList => MatchesBillResource(resource, source.Url, url),
            ApiParser.CongressHouseVoteList => MatchesHouseVoteResource(resource, source.Url, url),
            ApiParser.SenateVoteList => resource is Collection.ApiResource.SenateVoteMenu or Collection.ApiResource.SenateVote,
            ApiParser.VoteviewSenateVoteList => VoteviewUrl.TryReadCongress(source.Url, resource, url) is not null,
            ApiParser.CongressLegislators => resource == Collection.ApiResource.MemberRoster && url == source.Url,
            ApiParser.CongressLegislatorsExecutive => resource == Collection.ApiResource.ExecutiveRoster && url == source.Url,
            ApiParser.FederalRegisterDocuments => MatchesFederalRegisterResource(resource, source.Url, url),
            ApiParser.MemberPhotos => resource == Collection.ApiResource.MemberPhoto && MemberPhotoUrl.TryReadBioguideId(source.Url, url) is not null,
            ApiParser.CongressBillText => resource == Collection.ApiResource.BillText && BillTextUrl.TryReadPackageId(source.Url, url) is not null,
            _ => false
        };

    private static bool MatchesBillResource(ApiResource resource, string sourceUrl, string url) =>
        resource switch
        {
            Collection.ApiResource.BillList => BillStreamWindow.IsRequestUrl(sourceUrl, url),
            Collection.ApiResource.Bill => CongressBillListItem.IsDetailUrl(sourceUrl, url),
            Collection.ApiResource.BillCosponsors or Collection.ApiResource.BillActions or
            Collection.ApiResource.BillSummaries or Collection.ApiResource.BillSubjects or Collection.ApiResource.BillTextVersions =>
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

    private static bool MatchesFederalRegisterResource(ApiResource resource, string sourceUrl, string url) =>
        resource switch
        {
            Collection.ApiResource.FederalRegisterList => FederalRegisterUrl.TryReadList(sourceUrl, url) is not null,
            Collection.ApiResource.FederalRegisterDocument => FederalRegisterUrl.TryReadDocument(sourceUrl, url) is not null,
            _ => false
        };

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
