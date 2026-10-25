using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Application.Paging;
using CivicLens.Core.Actions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Tests.Application.Collection.Bills;

namespace CivicLens.Tests.Fixtures;

internal sealed record CosponsorSpec(string Bioguide, string Date, bool Original = false, string? Withdrawn = null);

internal sealed record SummarySpec(string Version, string ActionDate, string Updated, string Html);

/// <summary>One Congress.gov bill with real page bodies, the texts that preparation would have saved, and the stores that serve them.</summary>
internal sealed class ActionBillWorld
{
    public const string Warren = "W000817";
    public const string Markey = "M000133";
    public const string Stranger = "Z999999";
    public const string SourceId = "congress-bills";
    public const string SourceUrl = "https://api.congress.gov/v3/bill";
    public static readonly DateOnly Backfill = new(2025, 1, 3);
    public static readonly DateTimeOffset UpdatedAt = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, string> bodies = [];
    private readonly Dictionary<string, string> versionOverrides = [];
    private readonly Dictionary<string, CollectionJobRecord> jobs = [];
    private readonly List<(ApiResource Resource, string JobId, string Text)> pages = [];

    private ActionBillWorld(CollectionConfigurationRevision revision) => Revision = revision;

    public CollectionConfigurationRevision Revision { get; }
    public FixtureStores Stores { get; private set; } = null!;
    public IReadOnlyList<(ApiResource Resource, string JobId, string Text)> Pages => pages;

    public static string ExtraBioguide(int index) => "P" + index.ToString("D6");

    public static CollectionConfiguration Configuration(int extraPeople = 0, string warrenPersonId = "warren") => CongressBillSourceConfigurationTests.Configuration() with
    {
        People =
        [
            new() { Id = warrenPersonId, Name = "Elizabeth Warren", BioguideId = Warren },
            new() { Id = "markey", Name = "Ed Markey", BioguideId = Markey },
            new() { Id = "sanders", Name = "Bernie Sanders", BioguideId = "S000033" },
            new() { Id = "no-bioguide", Name = "Nobody Listed" },
            .. Enumerable.Range(1, extraPeople).Select(index => new PersonConfiguration { Id = "extra-" + index, Name = "Extra Person " + index, BioguideId = ExtraBioguide(index) })
        ]
    };

    public static ActionBillWorld Create(string[]? sponsors = null, CosponsorSpec[][]? cosponsorPages = null, SummarySpec[]? summaries = null,
        string? policyArea = BillGateFixtures.TrackedPolicyArea, DateTimeOffset? updatedAt = null, string title = "A housing bill", int extraPeople = 0,
        string warrenPersonId = "warren")
    {
        var world = new ActionBillWorld(CollectionConfigurationRevision.Create(Configuration(extraPeople, warrenPersonId)));
        sponsors ??= [Warren];
        cosponsorPages ??= [];
        summaries ??= [];
        var bill = new CongressBill(119, "hr", 1, title, Backfill, updatedAt ?? UpdatedAt, policyArea, sponsors, cosponsorPages.Sum(page => page.Length), 1,
            summaries.Length);
        world.Add(ApiResource.Bill, "detail", $"{SourceUrl}/119/hr/1?format=json", DetailBody(bill));
        for (var index = 0; index < cosponsorPages.Length; index++)
            world.Add(ApiResource.BillCosponsors, $"cosponsors-{index}", PageUrl("cosponsors", index, 250),
                CosponsorBody(cosponsorPages[index], index + 1 < cosponsorPages.Length ? PageUrl("cosponsors", index + 1, 250) : null));
        for (var index = 0; index < summaries.Length; index++)
            world.Add(ApiResource.BillSummaries, $"summaries-{index}", PageUrl("summaries", index, 1),
                SummaryBody(summaries[index], index + 1 < summaries.Length ? PageUrl("summaries", index + 1, 1) : null));
        world.Stores = new FixtureStores(world.jobs, world.bodies);
        return world;
    }

    public ActionBillReader Reader() => new(Stores, new ApiResponseReader(Stores, Stores), new FragmentExtractor(), "artifacts");

    public ActionDraftEvidence Evidence(ActionDraftRevision? current = null, DateTimeOffset? updatedAt = null, bool hasNewerCandidate = false) =>
        new(new ActionDraftCandidate(ActionRevisionFixtures.CandidateId, ActionRevisionFixtures.RecordId, SourceId, 119, "hr", 1,
            updatedAt ?? UpdatedAt, pages.Any(page => page.Resource == ApiResource.BillSummaries)),
            Revision, [.. pages.Select(page => new ActionDraftPage(page.Resource, page.JobId, ExtractionIdOf(page.JobId), versionOverrides.GetValueOrDefault(page.JobId) ?? ExtractionVersionOf(page.Resource), page.Text))], current, hasNewerCandidate);

    public static string ExtractionVersionOf(ApiResource resource) => resource switch
    {
        ApiResource.Bill => CongressBillParser.ExtractionVersion,
        ApiResource.BillCosponsors => CongressBillCosponsorsParser.ExtractionVersion,
        _ => CongressBillSummariesParser.ExtractionVersion(new FragmentExtractor())
    };

    public void SetVersion(string jobId, string extractionVersion)
    {
        versionOverrides[jobId] = extractionVersion;
    }

    public static string ExtractionIdOf(string jobId) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(jobId)));

    public string Quote(CivicLens.Core.Documents.SourceSpan span)
    {
        var page = pages.Single(item => ExtractionIdOf(item.JobId) == span.ExtractionId);
        return page.Text.Substring(span.Start, span.Length);
    }

    public void ReplaceCapturedBody(string jobId, string body)
    {
        bodies["attempt-" + jobId] = body;
    }

    public void TamperWithSavedText(string jobId, string text)
    {
        var index = pages.FindIndex(page => page.JobId == jobId);
        pages[index] = pages[index] with { Text = text };
    }

    private static string PageUrl(string segment, int index, int limit) => index == 0
        ? BillPageUrl.FirstPage(SourceUrl, segment == "cosponsors" ? ApiResource.BillCosponsors : ApiResource.BillSummaries, new(119, "hr", 1, default))
        : $"{SourceUrl}/119/hr/1/{segment}?offset={index * limit}&limit={limit}&format=json";

    private void Add(ApiResource resource, string jobId, string url, string body)
    {
        var configured = new ConfiguredCollectionSource(Revision, SourceId, Backfill);
        var definition = configured.CreateApiJobDefinition(resource, url, Backfill);
        var attempt = new CollectionJobAttempt("attempt-" + jobId, definition.CreateRequest(jobId, Path.GetFullPath("captures")),
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        jobs[jobId] = new CollectionJobRecord(jobId, definition, "key-" + jobId, CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null,
            false, [attempt]);
        bodies["attempt-" + jobId] = body;
        pages.Add((resource, jobId, RenderedText(resource, url, body)));
    }

    public static string RenderedText(ApiResource resource, string url, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var request = new ConfiguredCollectionSource(CollectionConfigurationRevision.Create(Configuration()), SourceId, Backfill)
            .CreateApiJobDefinition(resource, url, Backfill).CreateRequest("job", Path.GetFullPath("captures"));
        if (resource == ApiResource.Bill)
            return CongressBillRenderer.RenderDetail(CongressBillParser.Parse(bytes, CongressBillListItem.TryReadDetailUrl(SourceUrl, url)!));
        var bill = BillPageUrl.TryRead(SourceUrl, resource, url)!.Value.Bill;
        if (resource == ApiResource.BillCosponsors)
            return CongressBillRenderer.RenderCosponsors(bill, CongressBillCosponsorsParser.Parse(bytes, request, SourceUrl, bill));
        var page = CongressBillSummariesParser.Parse(bytes, request, SourceUrl, bill);
        return CongressBillRenderer.RenderSummaries(bill,
            [.. page.Summaries.Select(summary => new CongressBillSummaryText(summary, FragmentExtractor.Text(summary.Html)))]);
    }

    public static string DetailBody(CongressBill bill) =>
        JsonSerializer.Serialize(new
        {
            bill = new
            {
                congress = bill.Congress,
                type = bill.Type.ToUpperInvariant(),
                number = bill.Number.ToString(CultureInfo.InvariantCulture),
                title = bill.Title,
                introducedDate = bill.IntroducedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                updateDate = bill.UpdateDate.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                policyArea = bill.PolicyArea is null ? null : new { name = bill.PolicyArea },
                sponsors = bill.SponsorBioguideIds.Select(id => new { bioguideId = id }),
                cosponsors = new { countIncludingWithdrawnCosponsors = bill.CosponsorCount },
                actions = new { count = bill.ActionCount },
                summaries = new { count = bill.SummaryCount }
            }
        });

    public static string CosponsorBody(CosponsorSpec[] page, string? next) => JsonSerializer.Serialize(new
    {
        cosponsors = page.Select(item => new
        {
            bioguideId = item.Bioguide,
            sponsorshipDate = item.Date,
            isOriginalCosponsor = item.Original,
            sponsorshipWithdrawnDate = item.Withdrawn
        }),
        pagination = new { next }
    });

    public static string SummaryBody(SummarySpec summary, string? next) => JsonSerializer.Serialize(new
    {
        summaries = new[]
        {
            new { versionCode = summary.Version, actionDate = summary.ActionDate, actionDesc = "Introduced", updateDate = summary.Updated, text = summary.Html }
        },
        pagination = new { next }
    });

    internal sealed class FragmentExtractor : IDocumentTextExtractor
    {
        public string ExtractionVersion => "fake-extraction";

        public static string Text(string html) => "Text of " + html;

        public Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, DocumentContentProfile? profile,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ExtractHtmlFragmentAsync(string markup, CancellationToken cancellationToken) => Task.FromResult(Text(markup));
    }

    internal sealed class FixtureStores(Dictionary<string, CollectionJobRecord> jobs, Dictionary<string, string> bodies)
        : ICollectionJobStore, ICollectionAttemptStore, ICaptureBodyReader
    {
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) =>
            Task.FromResult(jobs.GetValueOrDefault(jobId));

        Task<StoredCollectionAttempt?> ICollectionAttemptStore.GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredCollectionAttempt?>(new StoredCollectionAttempt(
                new CapturedAttemptResult(attemptId, SourceId, SourceUrl, SourceUrl, DateTimeOffset.UnixEpoch,
                    new CollectionResponse(200, null, null, "application/json", []), new CaptureIdentity(new string('a', 64), 1)), null, null));

        public int BodyReads { get; private set; }

        public Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken)
        {
            BodyReads++;
            return Task.FromResult(Encoding.UTF8.GetBytes(bodies[attempt.AttemptId]));
        }

        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> RequestCheckAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(IReadOnlyCollection<string> sourceIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult?> ReadPacingBlockAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
