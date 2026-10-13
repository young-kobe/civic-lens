using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Issues;
using CivicLens.Application.Paging;
using CivicLens.Core.Collection;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;
using EvidenceStatus = CivicLens.Application.Collection.Processing.EvidenceProcessingStatus;

namespace CivicLens.Tests.Application.Collection.Processing;

public sealed class JobPreparationTests
{
    private const string SourceUrl = "https://api.congress.gov/v3/bill";
    private const string DetailUrl = SourceUrl + "/119/hr/1?format=json";
    private const string CosponsorUrl = SourceUrl + "/119/hr/1/cosponsors?format=json&limit=250";
    private const string ActionsUrl = SourceUrl + "/119/hr/1/actions?format=json&limit=250";
    private static readonly DateOnly Backfill = new(2025, 1, 3);
    private static readonly string ListUrl = new BillStreamWindow(Backfill, Backfill).RequestUrl(SourceUrl);
    private readonly RecordingBillAdmissions admissions = new();
    private readonly ThrowingProcessingStore processing = new();

    [Fact]
    public async Task DetailGateIsDecidedFromTheParsedBillAndStoredWithTheIssueRevision()
    {
        var revision = BillGateFixtures.Revision();

        var result = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.UntrackedPolicyArea, 0), revision);

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        var call = Assert.Single(admissions.Details);
        Assert.Equal(BillGateOutcome.Passed(BillGateReason.CoveredSponsor), call.Gate);
        Assert.Equal(revision.Id, call.IssueRevisionId);
        Assert.Equal(("job", 1), (call.JobId, call.Bill.Number));
        Assert.Equal((2, 1), (result.AdmittedCount, result.DuplicateCount));
    }

    [Theory]
    [InlineData("2025-01-02T19:00:00-05:00", BillGateDecision.Passed)]
    [InlineData("2025-01-03T04:00:00+05:00", BillGateDecision.NotRelevant)]
    public async Task CoverageIsReadOnTheUtcDayOfTheExactUpdateDate(string updateDate, BillGateDecision expected)
    {
        _ = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.UntrackedPolicyArea, 0, updateDate),
            BillGateFixtures.Revision());

        Assert.Equal(expected, Assert.Single(admissions.Details).Gate.Decision);
    }

    [Fact]
    public async Task ADetailJobWithoutAnIssuesConfigurationBlocksAndStoresNothing()
    {
        var result = await Prepare(ApiResource.Bill, DetailUrl, Detail("A000001", BillGateFixtures.TrackedPolicyArea, 0), null);

        Assert.Equal(EvidenceStatus.Blocked, result!.Status);
        Assert.Equal("issuesConfigurationUnavailable", result.ErrorCode);
        Assert.Empty(admissions.Details);
    }

    [Theory]
    [InlineData(ApiResource.Bill, DetailUrl)]
    [InlineData(ApiResource.BillCosponsors, CosponsorUrl)]
    [InlineData(ApiResource.BillActions, ActionsUrl)]
    public async Task AMalformedBillResponseIsInvalidDataSoTheWorkerBlocksThePermanentFailure(ApiResource resource, string url)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Prepare(resource, url, """{"unexpected":true}""", BillGateFixtures.Revision()));

        Assert.Empty(admissions.Details);
        Assert.Empty(admissions.CosponsorPages);
        Assert.Empty(admissions.ActionPages);
    }

    [Fact]
    public async Task ACosponsorPageIsParsedAgainstItsBillAndPassedToTheStoreWithItsJob()
    {
        _ = await Prepare(ApiResource.BillCosponsors, CosponsorUrl, Cosponsors("A000001", next: null), BillGateFixtures.Revision());

        var call = Assert.Single(admissions.CosponsorPages);
        Assert.Equal("job", call.JobId);
        Assert.Equal("A000001", Assert.Single(call.Page.Cosponsors).BioguideId);
        Assert.Null(call.Page.Next);
    }

    [Fact]
    public async Task AnActionsPageIsParsedAndPassedToTheStoreWithItsNextLink()
    {
        var next = ActionsUrl + "&offset=250";

        _ = await Prepare(ApiResource.BillActions, ActionsUrl, $$$"""{"actions":[{"actionDate":"2025-01-03","text":"Introduced"}],"pagination":{"next":"{{{next}}}"}}""",
            BillGateFixtures.Revision());

        var call = Assert.Single(admissions.ActionPages);
        Assert.Equal(next, call.Page.Next);
    }

    public static TheoryData<ApiResource?, string, string> EveryResource => new()
    {
        { null, SourceUrl, "{}" },
        { ApiResource.BillList, ListUrl, """{"bills":[],"pagination":{}}""" },
        { ApiResource.Bill, DetailUrl, Detail("Z000009", BillGateFixtures.TrackedPolicyArea, 0) },
        { ApiResource.BillCosponsors, CosponsorUrl, Cosponsors("Z000009", next: null) },
        { ApiResource.BillActions, ActionsUrl, """{"actions":[],"pagination":{}}""" }
    };

    [Fact]
    public void EveryApiResourceHasACaseInTheNeverExtractedTheory()
    {
        var covered = EveryResource.Select(row => (ApiResource?)row[0]).OfType<ApiResource>();

        Assert.Equal(Enum.GetValues<ApiResource>().Order(), covered.Order());
    }

    [Theory]
    [MemberData(nameof(EveryResource))]
    public async Task ApiJobsNeverGetAnExtractionStage(ApiResource? resource, string url, string body)
    {
        var result = await Prepare(resource, url, body, BillGateFixtures.Revision());

        Assert.Equal(EvidenceStatus.Succeeded, result!.Status);
        Assert.Equal(0, processing.EnsureCalls);
    }

    private async Task<JobPreparationResult?> Prepare(ApiResource? resource, string url, string body,
        IssueConfigurationRevision? issues)
    {
        var configured = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", Backfill);
        var definition = resource is null
            ? configured.CreateJobDefinition(Backfill)
            : configured.CreateApiJobDefinition(resource.Value, url, Backfill);
        var attempt = new CollectionJobAttempt("attempt", definition.CreateRequest("job", Path.GetFullPath("captures")),
            new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var job = new CollectionJobRecord("job", definition, "key", CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null,
            false, 0, 0, 0, [attempt]);
        var preparation = new JobPreparation(new SingleJob(job), new CapturedAttempts(), new NoDiscoveryAdmissions(), admissions,
            new FixedBody(body), processing, issues);
        return await preparation.PrepareAsync("job", "artifacts", default);
    }

    private static string Detail(string sponsor, string policyArea, int cosponsors, string updateDate = "2025-01-03T12:00:00Z") =>
        $$$"""
        {"bill":{"congress":119,"type":"HR","number":"1","title":"A bill","introducedDate":"2025-01-03",
         "updateDate":"{{{updateDate}}}","policyArea":{"name":"{{{policyArea}}}"},"sponsors":[{"bioguideId":"{{{sponsor}}}"}],
         "cosponsors":{"countIncludingWithdrawnCosponsors":{{{cosponsors}}} },"actions":{"count":1} } }
        """;

    private static string Cosponsors(string bioguideId, string? next)
    {
        var nextJson = next is null ? "null" : $"\"{next}\"";
        return $$$"""{"cosponsors":[{"bioguideId":"{{{bioguideId}}}","sponsorshipDate":"2025-01-04","isOriginalCosponsor":false}],"pagination":{"next":{{{nextJson}}}}}""";
    }

    private sealed class RecordingBillAdmissions : IBillStreamAdmissionStore
    {
        public List<(string JobId, CongressBill Bill, BillGateOutcome Gate, string IssueRevisionId)> Details { get; } = [];
        public List<(string JobId, CongressBillCosponsorsPage Page)> CosponsorPages { get; } = [];
        public List<(string JobId, CongressBillActionsPage Page)> ActionPages { get; } = [];

        public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
            BillStreamWindow window, CongressBillListPage page, CancellationToken cancellationToken) =>
            Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));

        public Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
            CongressBill bill, BillGateOutcome gate, string issueRevisionId, CancellationToken cancellationToken)
        {
            Details.Add((detailJobId, bill, gate, issueRevisionId));
            return Task.FromResult(new DiscoveryAdmissionResult(
                [new DiscoveryAdmissionJob("a", "https://x"), new DiscoveryAdmissionJob("b", "https://y")], 0, 1));
        }

        public Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(ConfiguredCollectionSource source, string pageJobId,
            CongressBillCosponsorsPage page, CancellationToken cancellationToken)
        {
            CosponsorPages.Add((pageJobId, page));
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }

        public Task<DiscoveryAdmissionResult> AdmitActionsPageAsync(ConfiguredCollectionSource source, string pageJobId,
            CongressBillActionsPage page, CancellationToken cancellationToken)
        {
            ActionPages.Add((pageJobId, page));
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }
    }

    private sealed class ThrowingProcessingStore : IEvidenceProcessingStore
    {
        public int EnsureCalls { get; private set; }

        public Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId,
            string requestedUrl, CancellationToken cancellationToken)
        {
            EnsureCalls++;
            throw new InvalidOperationException("API jobs have no extraction stage.");
        }

        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EvidenceProcessingRecord?> GetByAttemptIdAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsAwaitingPreparationAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionChangeEvent>> ListRecentChangesAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SingleJob(CollectionJobRecord job) : ICollectionJobStore
    {
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) =>
            Task.FromResult<CollectionJobRecord?>(job);

        public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(IReadOnlyCollection<SourceCheckTarget> targets, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturedAttempts : ICollectionAttemptStore
    {
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredCollectionAttempt?>(new StoredCollectionAttempt(
                new CapturedAttemptResult(attemptId, "congress-bills", "https://api.congress.gov/v3/bill",
                    "https://api.congress.gov/v3/bill", DateTimeOffset.UnixEpoch,
                    new CollectionResponse(200, null, null, "application/json", []), new CaptureIdentity(new string('a', 64), 1)),
                null, null));

        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoDiscoveryAdmissions : IDiscoveryAdmissionStore
    {
        public Task<DiscoveryAdmissionResult> AdmitAsync(ConfiguredCollectionSource source, string attemptId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DiscoveryAdmissionResult> AdmitAutomaticCheckAsync(ConfiguredCollectionSource source, string attemptId, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DiscoveryAdmissionResult> AdmitAsync(DiscoveryAdmissionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedBody(string body) : ICaptureBodyReader
    {
        public Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken) =>
            Task.FromResult(Encoding.UTF8.GetBytes(body));
    }
}
