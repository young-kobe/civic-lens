using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection;

/// <summary>A detached admission input whose omitted date is resolved after the saved key is read.</summary>
public sealed class ConfiguredCollectionSource
{
    public ConfiguredCollectionSource(CollectionConfiguration configuration, string sourceId, DateOnly? asOf = null)
        : this(CollectionConfigurationRevision.Create(configuration), sourceId, asOf)
    {
    }

    public ConfiguredCollectionSource(CollectionConfigurationRevision revision, string sourceId, DateOnly? asOf = null)
    {
        Revision = revision;
        SourceId = sourceId;
        AsOf = asOf;
        _ = ReadSource();
    }

    public CollectionConfigurationRevision Revision { get; }
    public string SourceId { get; }
    public DateOnly? AsOf { get; }
    public string SourceUrl => ReadSource().Url;

    public bool IsCollectedBySchedule => IsScheduleCollected(ReadSource());

    public static bool IsScheduleCollected(WatchedSourceConfiguration source) =>
        source.ApiParser == ApiParser.CongressBillList;

    public bool IsActiveOn(DateOnly day) => Revision.Configuration.IsActiveOn(ReadSource(), day);

    public CollectionJobDefinition CreateJobDefinition(DateOnly currentDate, CollectionJobDefinition? existing = null)
    {
        var source = ReadSource();
        if (source.ApiParser is null) return PrepareDefinition(source, currentDate, existing, article: false);
        var target = existing?.ApiResource is { } resource
            ? new ApiTarget(resource, existing.Url)
            : FirstEnqueuedApiTarget(source, currentDate);
        return PrepareDefinition(source, currentDate, existing, article: false, target);
    }

    public CollectionJobDefinition? CreateScheduledJobDefinition(DateTimeOffset now, DateOnly? billCursor)
    {
        var currentDate = DateOnly.FromDateTime(now.UtcDateTime);
        var source = ReadSource();
        if (source.ApiParser is null) return CreateJobDefinition(currentDate);
        var target = IsScheduleCollected(source)
            ? NextBillWindowTarget(source, now, billCursor)
            : FixedApiTarget(source, currentDate);
        return target is { } found ? PrepareDefinition(source, currentDate, null, article: false, found) : null;
    }

    public CollectionRequest CreateRequest(string jobId, string artifactDirectory, DateOnly currentDate)
    {
        var request = PrepareDefinition(ReadSource(), currentDate, null, article: false).CreateRequest(jobId, artifactDirectory);
        request.Validate();
        return request;
    }

    public DiscoveryAdmissionRequest CreateDiscoveryAdmission(string attemptId, string idempotencyKey,
        DateOnly currentDate, CollectionJobDefinition? existingTemplate = null, bool recheckKnownCandidates = false)
    {
        var source = ReadSource();
        if (source.Mode is not (CollectionMode.Feed or CollectionMode.Html))
            throw new ArgumentException("Admission requires a configured feed or HTML discovery source.");
        var template = PrepareDefinition(source, currentDate, existingTemplate, article: true);
        var request = new DiscoveryAdmissionRequest(attemptId, idempotencyKey, template,
            source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), source.Mode, recheckKnownCandidates);
        request.Validate();
        return request;
    }

    public CollectionJobDefinition CreateApiJobDefinition(ApiResource resource, string url, DateOnly currentDate) =>
        PrepareDefinition(ReadSource(), currentDate, null, article: false, new ApiTarget(resource, url));

    public BillStreamAdmission CreateBillStreamAdmission(string listJobId, BillStreamWindow window,
        CongressBillListPage page, DateOnly currentDate)
    {
        var source = ReadBillStreamSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        var bills = page.Bills.Where(bill => bill.Congress >= source.MinimumCongress)
            .Select(bill => new ApiJob(BillStreamJobKeys.Detail(SourceId, bill),
                template with { ApiResource = ApiResource.Bill, Url = bill.DetailUrl(source.Url) }))
            .DistinctBy(job => job.IdempotencyKey);
        var nextPage = page.Next is null
            ? null
            : new ApiJob(BillStreamJobKeys.NextPage(SourceId, listJobId),
                CreateApiJobDefinition(ApiResource.BillList, page.Next, currentDate));
        var policy = source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy();
        return new BillStreamAdmission(SourceId, policy, template, window, page.StartsPass, [.. bills], nextPage);
    }

    public IReadOnlySet<string> CoveredBioguideIdsOn(DateOnly day) =>
        Revision.Configuration.CoveredBioguideIdsOf(ReadSource(), day);

    public ApiJob CreateFirstPageJob(ApiResource resource, CongressBillListItem bill, DateTimeOffset updateDate,
        DateOnly currentDate) =>
        new(BillStreamJobKeys.FirstBillPage(SourceId, resource, bill, updateDate),
            CreateApiJobDefinition(resource, BillPageUrl.FirstPage(ReadBillStreamSource().Url, resource, bill), currentDate));

    public ApiJob CreateNextPageJob(ApiResource resource, string parentJobId, string nextUrl, DateOnly currentDate) =>
        new(BillStreamJobKeys.NextBillPage(SourceId, resource, parentJobId),
            CreateApiJobDefinition(resource, nextUrl, currentDate));

    public ApiJobBatch CreateHouseVoteListBatch(string listJobId, HouseVoteListPage page, DateOnly currentDate)
    {
        var source = ReadHouseVoteSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        var details = page.Votes
            .Select(vote => new ApiJob(HouseVoteJobKeys.Detail(SourceId, vote),
                template with { ApiResource = ApiResource.HouseVoteDetail, Url = HouseVoteRollUrl.Detail(source.Url, vote.Roll) }))
            .DistinctBy(job => job.IdempotencyKey);
        var nextUrl = page.Next ?? FollowingSessionUrl(source, page.Session, currentDate);
        var nextPage = nextUrl is null
            ? null
            : new ApiJob(HouseVoteJobKeys.NextPage(SourceId, listJobId),
                CreateApiJobDefinition(ApiResource.HouseVoteList, nextUrl, currentDate));
        return new ApiJobBatch(source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), template,
            nextPage is null ? [.. details] : [.. details, nextPage]);
    }

    public ApiJob CreateHouseVoteMembersJob(HouseVote vote, DateOnly currentDate) =>
        new(HouseVoteJobKeys.Members(SourceId, vote),
            CreateApiJobDefinition(ApiResource.HouseVoteMembers, HouseVoteRollUrl.Members(ReadHouseVoteSource().Url, vote.Roll),
                currentDate));

    public ApiJobBatch CreateApiBatch(IReadOnlyList<ApiJob> jobs, DateOnly currentDate)
    {
        var source = ReadApiSource();
        return new ApiJobBatch(source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(),
            PrepareDefinition(source, currentDate, null, article: false), jobs);
    }

    private CollectionJobDefinition PrepareDefinition(WatchedSourceConfiguration source, DateOnly currentDate,
        CollectionJobDefinition? existing, bool article, ApiTarget? target = null)
    {
        existing?.Validate();
        var definition = CollectionJobDefinition.FromSource(source);
        if (target is { } api)
            definition = definition with { ApiResource = api.Resource, Url = api.Url };
        if (article)
            definition = definition with { Mode = CollectionMode.Page, ETag = null, LastModified = null };

        if (existing is not null)
        {
            // Compare the request to the saved decision without reevaluating current eligibility.
            // Legacy keys retain unknown provenance and compare only their original execution settings.
            if (existing.ConfigurationRevision is not null)
            {
                definition = definition with
                {
                    ConfigurationRevision = Revision,
                    CoverageAsOf = AsOf ?? existing.CoverageAsOf
                };
            }
            if (definition != existing)
                throw new ArgumentException("The idempotency key belongs to different admission input.");
            return existing;
        }

        definition = definition with { ConfigurationRevision = Revision, CoverageAsOf = AsOf ?? currentDate };
        definition.Validate();
        return definition;
    }

    private static ApiTarget FirstEnqueuedApiTarget(WatchedSourceConfiguration source, DateOnly currentDate) =>
        IsScheduleCollected(source)
            ? throw new ArgumentException("A bill list source is collected only by its schedule.")
            : FixedApiTarget(source, currentDate)
                ?? throw new ArgumentException("The API source has no work to collect now.");

    private static ApiTarget? NextBillWindowTarget(WatchedSourceConfiguration source, DateTimeOffset now,
        DateOnly? billCursor) =>
        BillStreamWindow.Next(billCursor, source.BackfillFrom!.Value, now) is { } window
            ? new ApiTarget(ApiResource.BillList, window.RequestUrl(source.Url))
            : null;

    private static ApiTarget? FixedApiTarget(WatchedSourceConfiguration source, DateOnly today)
    {
        switch (source.ApiParser)
        {
            case ApiParser.CongressHouseVoteList:
                return HouseVoteSession.FirstOfSweep(source.BackfillFrom!.Value, today, source.MinimumCongress!.Value) is { } first
                    ? new ApiTarget(ApiResource.HouseVoteList, HouseVoteListUrl.FirstPage(source.Url, first))
                    : null;
            case ApiParser.CongressLegislators:
                return new ApiTarget(ApiResource.MemberRoster, source.Url);
            case ApiParser.CongressLegislatorsExecutive:
                return new ApiTarget(ApiResource.ExecutiveRoster, source.Url);
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private readonly record struct ApiTarget(ApiResource Resource, string Url);

    private static string? FollowingSessionUrl(WatchedSourceConfiguration source, HouseVoteSession session, DateOnly today) =>
        session.Following(today) is { } following ? HouseVoteListUrl.FirstPage(source.Url, following) : null;

    private WatchedSourceConfiguration ReadHouseVoteSource() =>
        ReadSource() is { ApiParser: ApiParser.CongressHouseVoteList } source
            ? source
            : throw new ArgumentException("House vote admission requires a configured Congress House vote source.");

    private WatchedSourceConfiguration ReadApiSource() =>
        ReadSource() is { ApiParser: not null } source
            ? source
            : throw new ArgumentException("API admission requires a configured Congress API source.");

    private WatchedSourceConfiguration ReadBillStreamSource() =>
        ReadSource() is { ApiParser: ApiParser.CongressBillList } source
            ? source
            : throw new ArgumentException("Bill stream admission requires a configured Congress bill list source.");

    private WatchedSourceConfiguration ReadSource() =>
        Revision.Configuration.Sources.SingleOrDefault(source => source.Id == SourceId)
        ?? throw new ArgumentException("Unknown source ID.", nameof(SourceId));
}
