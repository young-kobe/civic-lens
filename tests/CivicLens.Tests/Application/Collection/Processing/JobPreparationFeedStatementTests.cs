using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using static CivicLens.Tests.Application.Collection.FeedStatements.FeedItemParserTests;

namespace CivicLens.Tests.Application.Collection.Processing;

public sealed class JobPreparationFeedStatementTests
{
    private const string FeedUrl = "https://senator.test/news/feed";
    private static readonly DateOnly Today = new(2026, 10, 29);
    private readonly RecordingAdmissions admissions = new();
    private readonly RecordingExtractions extractions = new();

    [Fact]
    public async Task AFlaggedFeedSavesItsItemsWithTheAdmissionLimitOfTheSourceAndStillAdmitsThePageJobs()
    {
        var feed = Feed(Item(link: "https://senator.test/news/a", description: "text"));

        var result = await PrepareAsync(feed, flagged: true, maxJobs: 3);

        Assert.Equal(EvidenceProcessingStatus.Succeeded, result!.Status);
        Assert.Equal(1, admissions.Calls);
        Assert.Equal(["https://senator.test/news/a"], extractions.Items.Select(item => item.Link));
        Assert.Equal(3, extractions.Limit);
    }

    [Fact]
    public async Task AFeedThatIsNotFlaggedSavesNoItemTextBecauseAPersonMustCheckTheFeedFirst()
    {
        var result = await PrepareAsync(Feed(Item(link: "https://senator.test/news/a", description: "text")), flagged: false, maxJobs: 3);

        Assert.Equal(EvidenceProcessingStatus.Succeeded, result!.Status);
        Assert.Equal(0, extractions.SaveCalls);
    }

    [Fact]
    public async Task AFeedThatCannotBeReadBlocksTheJobWithACodeAndKeepsThePageAdmission()
    {
        var result = await PrepareAsync(Encoding.UTF8.GetBytes("<rss"), flagged: true, maxJobs: 3);

        Assert.Equal((EvidenceProcessingStatus.Blocked, "feedStatementTextInvalid"), (result!.Status, result.ErrorCode));
        Assert.Equal(1, admissions.Calls);
        Assert.Equal(0, extractions.SaveCalls);
    }

    private async Task<JobPreparationResult?> PrepareAsync(byte[] body, bool flagged, int maxJobs)
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Person" }],
            Sources =
            [
                new WatchedSourceConfiguration
                {
                    Id = "senator",
                    Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
                    Url = FeedUrl,
                    AllowedOrigin = "https://senator.test",
                    AllowedPathPrefix = "/news",
                    Mode = CollectionMode.Feed,
                    StatementTextInFeed = flagged,
                    AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = maxJobs }
                }
            ]
        };
        var definition = new ConfiguredCollectionSource(CollectionConfigurationRevision.Create(configuration), "senator", Today)
            .CreateJobDefinition(Today);
        var request = definition.CreateRequest("job", Path.GetFullPath("captures"));
        var attempt = new CollectionJobAttempt("attempt", request, new CollectionAttemptResolution(CollectionJobAttemptOutcome.Succeeded, null),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var job = new CollectionJobRecord("job", definition, "key", CollectionJobState.Succeeded, DateTimeOffset.UnixEpoch, null, false, [attempt]);
        var captured = new CapturedAttemptResult("attempt", "senator", FeedUrl, FeedUrl, DateTimeOffset.UnixEpoch,
            new CollectionResponse(200, null, null, "application/rss+xml", []), new CaptureIdentity(new string('a', 64), 1));
        var stored = new StoredCollectionAttempt(captured, null, null,
            new DiscoveryEvidence(request, new DiscoveryResult { Status = DiscoveryStatus.Parsed, Urls = [], OutOfScopeCount = 0 }));
        var preparation = new JobPreparation(new FixedJobStore { Get = (_, _) => Task.FromResult<CollectionJobRecord?>(job) },
            new FixedAttempt(stored), admissions, null!, null!, null!, null!, new FixedBody(body), null!, extractions,
            new Extractor(), null, NullLogger<JobPreparation>.Instance);
        return await preparation.PrepareAsync("job", "artifacts", default);
    }

    private sealed class FixedAttempt(StoredCollectionAttempt stored) : ICollectionAttemptStore
    {
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult<StoredCollectionAttempt?>(stored);

        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedBody(byte[] body) : ICaptureBodyReader
    {
        public Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken) =>
            Task.FromResult(body);
    }

    private sealed class RecordingAdmissions : IDiscoveryAdmissionStore
    {
        public int Calls { get; private set; }

        public Task<DiscoveryAdmissionResult> AdmitAsync(ConfiguredCollectionSource source, string attemptId, string idempotencyKey,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DiscoveryAdmissionResult> AdmitAutomaticCheckAsync(ConfiguredCollectionSource source, string attemptId,
            string idempotencyKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new DiscoveryAdmissionResult([], 0, 0));
        }
    }

    private sealed class RecordingExtractions : IDocumentExtractionStore
    {
        public List<FeedItemExtraction> Items { get; } = [];
        public int Limit { get; private set; }
        public int SaveCalls { get; private set; }

        public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> SaveFeedItemsAsync(IReadOnlyList<FeedItemExtraction> newestFirst, int maximumNew, CancellationToken cancellationToken)
        {
            SaveCalls++;
            Items.AddRange(newestFirst);
            Limit = maximumNew;
            return Task.FromResult(newestFirst.Count);
        }
    }

    private sealed class Extractor : IDocumentTextExtractor
    {
        public string ExtractionVersion => "fake-extraction";

        public Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, DocumentContentProfile? profile,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ExtractHtmlFragmentAsync(string markup, CancellationToken cancellationToken) => Task.FromResult(markup);
    }
}
