using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Collection.VoteviewVotes;
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
            ? new ApiTarget(resource, existing.Url, CollectionJobPriority.Check)
            : FirstEnqueuedApiTarget(source, currentDate);
        return PrepareDefinition(source, currentDate, existing, article: false, target);
    }

    public CollectionJobDefinition? CreateScheduledJobDefinition(DateTimeOffset now, DateOnly? billCursor)
    {
        var currentDate = DateOnly.FromDateTime(now.UtcDateTime);
        var source = ReadSource();
        if (source.ApiParser is null) return CreateJobDefinition(currentDate);
        var target = IsScheduleCollected(source)
            ? NextBillWindowTarget(source, now, billCursor, currentDate)
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

    public CollectionJobDefinition CreateApiJobDefinition(ApiResource resource, string url, DateOnly currentDate,
        int priority) =>
        PrepareDefinition(ReadSource(), currentDate, null, article: false, new ApiTarget(resource, url, priority));

    public BillStreamAdmission CreateBillStreamAdmission(string listJobId, BillStreamWindow window,
        CongressBillListPage page, DateOnly currentDate)
    {
        var source = ReadBillStreamSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        var bills = page.Bills.Where(bill => bill.Congress >= source.MinimumCongress)
            .Select(bill => new ApiJob(BillStreamJobKeys.Detail(SourceId, bill),
                template with
                {
                    ApiResource = ApiResource.Bill,
                    Url = bill.DetailUrl(source.Url),
                    Priority = CollectionJobPriority.OfBillDay(bill.UpdateDate, currentDate)
                }))
            .DistinctBy(job => job.IdempotencyKey);
        var nextPage = page.Next is null
            ? null
            : new ApiJob(BillStreamJobKeys.NextPage(SourceId, listJobId),
                CreateApiJobDefinition(ApiResource.BillList, page.Next, currentDate,
                    CollectionJobPriority.OfBillDay(window.LastDay, currentDate)));
        var policy = source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy();
        return new BillStreamAdmission(SourceId, policy, template, window, page.StartsPass, [.. bills], nextPage);
    }

    public IReadOnlySet<string> CoveredBioguideIdsOn(DateOnly day) =>
        Revision.Configuration.CoveredBioguideIdsOf(ReadSource(), day);

    public ApiJob CreateFirstPageJob(ApiResource resource, CongressBillListItem bill, DateTimeOffset updateDate,
        DateOnly currentDate) =>
        new(BillStreamJobKeys.FirstBillPage(SourceId, resource, bill, updateDate),
            CreateApiJobDefinition(resource, BillPageUrl.FirstPage(ReadBillStreamSource().Url, resource, bill),
                currentDate, CollectionJobPriority.OfBillDay(bill.UpdateDate, currentDate)));

    public ApiJob CreateNextPageJob(ApiResource resource, string parentJobId, string nextUrl, DateOnly billDay,
        DateOnly currentDate) =>
        new(BillStreamJobKeys.NextBillPage(SourceId, resource, parentJobId),
            CreateApiJobDefinition(resource, nextUrl, currentDate, CollectionJobPriority.OfBillDay(billDay, currentDate)));

    public ApiJobBatch CreateHouseVoteListBatch(string listJobId, HouseVoteListPage page, DateOnly currentDate)
    {
        var source = ReadHouseVoteSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        var details = page.Votes
            .Select(vote => new ApiJob(HouseVoteJobKeys.Detail(SourceId, vote),
                template with
                {
                    ApiResource = ApiResource.HouseVoteDetail,
                    Url = HouseVoteRollUrl.Detail(source.Url, vote.Roll),
                    Priority = CollectionJobPriority.OfSession(vote.Roll.Session, currentDate)
                }))
            .DistinctBy(job => job.IdempotencyKey);
        var following = page.Next is null ? page.Session.Following(currentDate) : null;
        var nextUrl = page.Next ?? (following is { } session ? HouseVoteListUrl.FirstPage(source.Url, session) : null);
        var nextPage = nextUrl is null
            ? null
            : new ApiJob(HouseVoteJobKeys.NextPage(SourceId, listJobId),
                CreateApiJobDefinition(ApiResource.HouseVoteList, nextUrl, currentDate, CollectionJobPriority.Check));
        return new ApiJobBatch(source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), template,
            nextPage is null ? [.. details] : [.. details, nextPage]);
    }

    public ApiJob CreateHouseVoteMembersJob(HouseVote vote, DateOnly currentDate) =>
        new(HouseVoteJobKeys.Members(SourceId, vote),
            CreateApiJobDefinition(ApiResource.HouseVoteMembers, HouseVoteRollUrl.Members(ReadHouseVoteSource().Url, vote.Roll),
                currentDate, CollectionJobPriority.OfSession(vote.Roll.Session, currentDate)));

    public ApiJobBatch CreateSenateMenuBatch(string menuJobId, SenateVoteMenu menu, DateOnly currentDate)
    {
        var source = ReadSenateVoteSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        var votes = menu.Votes
            .Select(item => new ApiJob(SenateVoteJobKeys.Vote(SourceId, item, currentDate),
                template with
                {
                    ApiResource = ApiResource.SenateVote,
                    Url = SenateVoteUrl.Vote(source.Url, item.Roll),
                    Priority = CollectionJobPriority.OfSession(item.Roll.Session, currentDate)
                }));
        var following = menu.Session.Following(currentDate);
        var nextMenu = following is not { } nextSession
            ? null
            : new ApiJob(SenateVoteJobKeys.NextMenu(SourceId, menuJobId),
                CreateApiJobDefinition(ApiResource.SenateVoteMenu, SenateVoteUrl.Menu(source.Url, nextSession),
                    currentDate, CollectionJobPriority.Check));
        return new ApiJobBatch(source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), template,
            nextMenu is null ? [.. votes] : [.. votes, nextMenu]);
    }

    public ApiJobBatch CreateVoteviewFollowOnBatch(ApiResource finished, string jobId, int congress, DateOnly currentDate)
    {
        var source = ReadVoteviewSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        return new ApiJobBatch(source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), template,
            [.. VoteviewFollowOns(source, finished, jobId, congress, currentDate)]);
    }

    private IEnumerable<ApiJob> VoteviewFollowOns(WatchedSourceConfiguration source, ApiResource finished, string jobId, int congress,
        DateOnly currentDate)
    {
        switch (finished)
        {
            case ApiResource.VoteviewRollCalls:
                yield return VoteviewJob(source, ApiResource.VoteviewMembers, VoteviewJobKeys.Members(SourceId, jobId), congress, currentDate);
                if (congress < VoteviewJobKeys.CurrentCongress(currentDate))
                    yield return VoteviewJob(source, ApiResource.VoteviewRollCalls, VoteviewJobKeys.RollCalls(SourceId, jobId),
                        congress + 1, currentDate);
                break;
            case ApiResource.VoteviewMembers:
                yield return VoteviewJob(source, ApiResource.VoteviewVotes, VoteviewJobKeys.Votes(SourceId, jobId), congress, currentDate);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(finished));
        }
    }

    private ApiJob VoteviewJob(WatchedSourceConfiguration source, ApiResource resource, string key, int congress, DateOnly currentDate) =>
        new(key, CreateApiJobDefinition(resource, VoteviewUrl.File(source.Url, resource, congress), currentDate, CollectionJobPriority.Check));

    public ApiJobBatch CreateMemberPhotoBatch(DateOnly currentDate)
    {
        var source = ReadMemberPhotoSource();
        var template = PrepareDefinition(source, currentDate, null, article: false);
        var photos = Revision.Configuration.People
            .Where(person => person.BioguideId is not null)
            .Select(person => new ApiJob(MemberPhotoJobKeys.Photo(SourceId, person.BioguideId!, currentDate),
                template with
                {
                    ApiResource = ApiResource.MemberPhoto,
                    Url = MemberPhotoUrl.Photo(source.Url, person.BioguideId!),
                    Priority = CollectionJobPriority.Backfill
                }));
        return new ApiJobBatch(source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy(), template, [.. photos]);
    }

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
            definition = definition with { ApiResource = api.Resource, Url = api.Url, Priority = api.Priority };
        if (article)
            definition = definition with { Mode = CollectionMode.Page, ETag = null, LastModified = null };

        if (existing is not null)
        {
            // Compare the request to the saved decision without reevaluating current eligibility.
            definition = definition with
            {
                ConfigurationRevision = Revision,
                CoverageAsOf = AsOf ?? existing.CoverageAsOf,
                Priority = existing.Priority
            };
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
        DateOnly? billCursor, DateOnly today) =>
        BillStreamWindow.Next(billCursor, source.BackfillFrom!.Value, now) is { } window
            ? new ApiTarget(ApiResource.BillList, window.RequestUrl(source.Url), CollectionJobPriority.OfBillDay(window.LastDay, today))
            : null;

    private static ApiTarget? FixedApiTarget(WatchedSourceConfiguration source, DateOnly today)
    {
        switch (source.ApiParser)
        {
            case ApiParser.CongressHouseVoteList:
                return VoteSession.FirstOfSweep(source.BackfillFrom!.Value, today, source.MinimumCongress!.Value) is { } first
                    ? new ApiTarget(ApiResource.HouseVoteList, HouseVoteListUrl.FirstPage(source.Url, first),
                        CollectionJobPriority.Check)
                    : null;
            case ApiParser.SenateVoteList:
                return VoteSession.FirstOfSweep(source.BackfillFrom!.Value, today, source.MinimumCongress!.Value) is { } firstMenu
                    ? new ApiTarget(ApiResource.SenateVoteMenu, SenateVoteUrl.Menu(source.Url, firstMenu),
                        CollectionJobPriority.Check)
                    : null;
            case ApiParser.VoteviewSenateVoteList:
                return VoteSession.FirstOfSweep(source.BackfillFrom!.Value, today, source.MinimumCongress!.Value) is { } firstSession
                    ? new ApiTarget(ApiResource.VoteviewRollCalls,
                        VoteviewUrl.File(source.Url, ApiResource.VoteviewRollCalls, firstSession.Congress), CollectionJobPriority.Check)
                    : null;
            case ApiParser.CongressLegislators:
                return new ApiTarget(ApiResource.MemberRoster, source.Url, CollectionJobPriority.Check);
            case ApiParser.CongressLegislatorsExecutive:
                return new ApiTarget(ApiResource.ExecutiveRoster, source.Url, CollectionJobPriority.Check);
            case ApiParser.MemberPhotos:
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private readonly record struct ApiTarget(ApiResource Resource, string Url, int Priority);

    private WatchedSourceConfiguration ReadHouseVoteSource() =>
        ReadSource() is { ApiParser: ApiParser.CongressHouseVoteList } source
            ? source
            : throw new ArgumentException("House vote admission requires a configured Congress House vote source.");

    private WatchedSourceConfiguration ReadSenateVoteSource() =>
        ReadSource() is { ApiParser: ApiParser.SenateVoteList } source
            ? source
            : throw new ArgumentException("Senate vote admission requires a configured Senate vote source.");

    private WatchedSourceConfiguration ReadVoteviewSource() =>
        ReadSource() is { ApiParser: ApiParser.VoteviewSenateVoteList } source
            ? source
            : throw new ArgumentException("Voteview admission requires a configured Voteview Senate vote source.");

    private WatchedSourceConfiguration ReadMemberPhotoSource() =>
        ReadSource() is { ApiParser: ApiParser.MemberPhotos } source
            ? source
            : throw new ArgumentException("Member photo admission requires a configured member photo source.");

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
