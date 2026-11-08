using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.Bills;

public sealed class BackfillBillActionsTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CapturedProvenanceMustMatchTheExactRequestEvenWhenTheAttemptIdMatches(bool changeSource)
    {
        var world = ActionBillWorld.Create();
        world.AddActionsPage("actions-0", 0, ActionBillWorld.ActionsBody(["Captured."], null));
        var original = (await ExtractionsForAsync(world, "actions-0")).Items[0];
        var captured = original.SourceAttempt;
        var substituted = new CapturedAttemptResult(captured.AttemptId,
            changeSource ? "another-source" : captured.SourceId,
            changeSource ? captured.RequestedUrl : captured.RequestedUrl + "&other=1",
            captured.FinalUrl, captured.ObservedAt, captured.Response, captured.Capture);
        var extraction = new DocumentExtraction(substituted, original.ExtractionVersion, original.Text);
        var store = new RecordingStore([new("actions-0", 0, extraction.ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)]);

        var result = await Backfill(world, store, new RecordingExtractions([extraction])).ExecuteAsync(Owner, 1);

        Assert.Equal(captured.AttemptId, extraction.SourceAttempt.AttemptId);
        Assert.Equal(new BillActionsBackfillResult(1, 0, 1), result);
        Assert.Empty(store.RecordedChains);
        Assert.Equal(0, world.Stores.BodyReads);
    }

    [Fact]
    public async Task AStoredUnsupportedEncodingCountsAsFailedAndTheNextBillStillRecovers()
    {
        var world = ActionBillWorld.Create();
        world.AddActionsPage("bad-actions", 0, ActionBillWorld.ActionsBody(["Unreadable."], null));
        world.AddActionsPage("actions-0", 0, ActionBillWorld.ActionsBody(["Recovered."], null));
        var extractions = await ExtractionsForAsync(world, "bad-actions", "actions-0");
        var store = new RecordingStore([new("actions-0", 0, extractions.Items[1].ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)])
        {
            DetailIds = ["bad-detail", "detail"],
            OtherPages = new Dictionary<string, IReadOnlyList<BillActionsBackfillPage>>
            {
                ["bad-detail"] = [new("bad-actions", 0, extractions.Items[0].ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)]
            }
        };
        var backfill = new BackfillBillActions(store, world.Stores, extractions,
            new ApiResponseReader(world.Stores, new UnsupportedEncodingBodies(world.Stores)), "artifacts");

        var result = await backfill.ExecuteAsync(Owner, 1);

        Assert.Equal(new BillActionsBackfillResult(2, 1, 1), result);
        Assert.Equal("Recovered.", Assert.Single(Assert.Single(store.RecordedChains)[0].Page.Actions).Text);
    }

    [Fact]
    public async Task AFailedBillDoesNotPreventTheNextKeyFromBeingRecoveredWithinTheLimit()
    {
        var world = ActionBillWorld.Create();
        world.AddActionsPage("actions-0", 0, ActionBillWorld.ActionsBody(["Recovered."], null));
        var extractions = await ExtractionsForAsync(world, "actions-0");
        var store = new RecordingStore([new("actions-0", 0, extractions.Items[0].ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)])
        { DetailIds = ["a-missing", "detail"] };

        var result = await Backfill(world, store, extractions).ExecuteAsync(Owner, 1);

        Assert.Equal(new BillActionsBackfillResult(2, 1, 1), result);
        Assert.Single(store.RecordedChains);
        Assert.Equal([(null, 1), ("a-missing", 1)], store.ListCalls);
    }

    [Fact]
    public async Task RecoveryReadsEachPageFromItsPinnedSavedAttemptAndSubmitsTheCompleteChain()
    {
        var world = ActionBillWorld.Create();
        var secondUrl = $"{ActionBillWorld.SourceUrl}/119/hr/1/actions?offset=250&limit=250&format=json";
        world.AddActionsPage("actions-0", 0, ActionBillWorld.ActionsBody(["First."], secondUrl));
        world.AddActionsPage("actions-1", 250, ActionBillWorld.ActionsBody(["Second."], null));
        var extractionStore = await ExtractionsForAsync(world, "actions-0", "actions-1");
        var store = new RecordingStore(
            [new("actions-0", 0, extractionStore.Items[0].ExtractionId, false, 119, "hr", 1, ActionBillWorld.SourceId),
                new("actions-1", 1, extractionStore.Items[1].ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)]);

        var result = await Backfill(world, store, extractionStore).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillActionsBackfillResult(1, 1, 0), result);
        Assert.Equal(2, Assert.Single(store.RecordedChains).Count);
        Assert.Equal(["First."], store.RecordedChains[0][0].Page.Actions.Select(action => action.Text));
        Assert.Equal(["Second."], store.RecordedChains[0][1].Page.Actions.Select(action => action.Text));
        Assert.Equal(2, world.Stores.BodyReads);
    }

    [Fact]
    public async Task ANewerSuccessfulAttemptCannotReplaceTheExactAttemptPinnedByAnExtraction()
    {
        var world = ActionBillWorld.Create();
        world.AddActionsPage("actions-0", 0, ActionBillWorld.ActionsBody(["Captured."], null));
        var newerCapture = new CapturedAttemptResult("newer-success", ActionBillWorld.SourceId,
            $"{ActionBillWorld.SourceUrl}/119/hr/1/actions?limit=250&format=json", ActionBillWorld.SourceUrl,
            DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "application/json", []),
            new CaptureIdentity(new string('b', 64), 9));
        var wrongExtraction = new DocumentExtraction(newerCapture, CongressBillActionsParser.ExtractionVersion, "saved text");
        var extractionStore = new RecordingExtractions([wrongExtraction]);
        var store = new RecordingStore([new("actions-0", 0, wrongExtraction.ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)]);

        var result = await Backfill(world, store, extractionStore).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillActionsBackfillResult(1, 0, 1), result);
        Assert.Empty(store.RecordedChains);
        Assert.Equal(0, world.Stores.BodyReads);
    }

    [Fact]
    public async Task AnEarlyClosingPageRefusesTheWholeChainBeforeTheStoreWrites()
    {
        var world = ActionBillWorld.Create();
        world.AddActionsPage("actions-0", 0, ActionBillWorld.ActionsBody(["First."], null));
        world.AddActionsPage("actions-1", 250, ActionBillWorld.ActionsBody(["Second."], null));
        var extractionStore = await ExtractionsForAsync(world, "actions-0", "actions-1");
        var store = new RecordingStore(
            [new("actions-0", 0, extractionStore.Items[0].ExtractionId, false, 119, "hr", 1, ActionBillWorld.SourceId),
                new("actions-1", 1, extractionStore.Items[1].ExtractionId, true, 119, "hr", 1, ActionBillWorld.SourceId)]);

        var result = await Backfill(world, store, extractionStore).ExecuteAsync(Owner, 10);

        Assert.Equal(new BillActionsBackfillResult(1, 0, 1), result);
        Assert.Empty(store.RecordedChains);
    }

    private static async Task<RecordingExtractions> ExtractionsForAsync(ActionBillWorld world, params string[] jobIds)
    {
        var extractions = new List<DocumentExtraction>();
        var attempts = (ICollectionAttemptStore)world.Stores;
        foreach (var jobId in jobIds)
        {
            var stored = await attempts.GetAsync("attempt-" + jobId, default);
            var captured = Assert.IsType<CapturedAttemptResult>(stored!.AttemptResult);
            var request = (await world.Stores.GetAsync(jobId, default))!.Attempts.Single().Request;
            var pinned = new CapturedAttemptResult(captured.AttemptId, request.SourceId, request.Url,
                captured.FinalUrl, captured.ObservedAt, captured.Response, captured.Capture);
            extractions.Add(new DocumentExtraction(pinned, CongressBillActionsParser.ExtractionVersion, "saved text"));
        }
        return new RecordingExtractions(extractions);
    }

    private static BackfillBillActions Backfill(ActionBillWorld world, RecordingStore store, RecordingExtractions extractions) =>
        new(store, world.Stores, extractions, new ApiResponseReader(world.Stores, world.Stores), "artifacts");

    private sealed class RecordingExtractions(IReadOnlyList<DocumentExtraction> items) : IDocumentExtractionStore
    {
        public IReadOnlyList<DocumentExtraction> Items { get; } = items;

        public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.ExtractionId == extractionId));

        public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveFeedItemsAsync(IReadOnlyList<FeedItemExtraction> newestFirst, int maximumNew,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingStore(IReadOnlyList<BillActionsBackfillPage> pages) : IBillActionsBackfillStore
    {
        public string[] DetailIds { get; init; } = ["detail"];
        public IReadOnlyDictionary<string, IReadOnlyList<BillActionsBackfillPage>> OtherPages { get; init; } =
            new Dictionary<string, IReadOnlyList<BillActionsBackfillPage>>();
        public List<IReadOnlyList<VerifiedBillActionsPage>> RecordedChains { get; } = [];
        public List<(string? After, int Limit)> ListCalls { get; } = [];

        public Task<IReadOnlyList<string>> ListBillsWithUnverifiedActionsAsync(string? after, int limit, CancellationToken cancellationToken)
        {
            ListCalls.Add((after, limit));
            return Task.FromResult<IReadOnlyList<string>>([.. DetailIds
                .Where(id => after is null || string.CompareOrdinal(id, after) > 0).Take(limit)]);
        }

        public Task<IReadOnlyList<BillActionsBackfillPage>> ListActionPagesAsync(string detailJobId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BillActionsBackfillPage>>(
                detailJobId == "detail" ? pages : OtherPages.GetValueOrDefault(detailJobId) ?? []);

        public Task<bool> RecordVerifiedActionChainAsync(string detailJobId, IReadOnlyList<VerifiedBillActionsPage> pages,
            CancellationToken cancellationToken)
        {
            RecordedChains.Add(pages);
            return Task.FromResult(true);
        }
    }

    private sealed class UnsupportedEncodingBodies(ICaptureBodyReader inner) : ICaptureBodyReader
    {
        public Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken) =>
            attempt.AttemptId == "attempt-bad-actions"
                ? throw new NotSupportedException("Unsupported stored encoding.")
                : inner.ReadAsync(attempt, artifactRoot, cancellationToken);
    }
}
