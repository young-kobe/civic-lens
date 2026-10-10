using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeAnalysisWorkerTests
{
    private static readonly DocumentChangeAnalysisCatalog Catalog = new([], []);

    [Fact]
    public async Task AnUnexpectedModelErrorAfterTheReservationFailsTheEntryAndKeepsTheCharge()
    {
        var store = new FakeAnalysisStore(1);
        var model = new FakeDocumentChangeDraftingModel().Reply((_, _) => throw new InvalidOperationException("Broken."));

        await Worker(store, model).ExecuteAsync(once: true);

        var entry = store.Entries.Single();
        Assert.Equal((State.Failed, "InvalidOperationException"), (entry.Status, entry.ErrorCode));
        var run = Assert.Single(store.Runs);
        Assert.Equal(AnalysisRunOutcome.ProcessingFailed, run.Outcome);
        Assert.Equal(model.Requests[0].ReservedTokens, store.Charged);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException), State.RetryWaiting)]
    [InlineData(typeof(InvalidDataException), State.Blocked)]
    public async Task AnEvidenceReadErrorBeforeTheReservationNeverCallsTheModel(Type error, State expected)
    {
        var store = new FakeAnalysisStore(1) { ReadEvidence = _ => throw (Exception)Activator.CreateInstance(error, "Broken.")! };
        var model = new FakeDocumentChangeDraftingModel();

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Empty(store.Runs);
        Assert.Equal((expected, error.Name), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    [Theory]
    [InlineData(DocumentChangeDraftingFailure.AuthenticationFailed, "authenticationFailed")]
    [InlineData(DocumentChangeDraftingFailure.BillingFailed, "billingFailed")]
    [InlineData(DocumentChangeDraftingFailure.ModelNotFound, "modelNotFound")]
    public async Task AnAccountErrorPausesDraftingUntilRestartWithoutCostingTheEntryAnAttempt(DocumentChangeDraftingFailure failure,
        string reason)
    {
        var store = new FakeAnalysisStore(2);
        var status = new FakeAnalysisStatusStore();
        var model = new FakeDocumentChangeDraftingModel().Fail(failure);

        await Worker(store, model, status: status).ExecuteAsync(once: true);

        Assert.Single(model.Requests);
        Assert.All(store.Entries, entry => Assert.Equal((State.Pending, 0), (entry.Status, entry.Attempts)));
        Assert.Equal((reason, (TimeSpan?)null), Assert.Single(status.Pauses));
    }

    [Fact]
    public async Task RepeatedProviderFailuresPauseDraftingForABoundedTime()
    {
        var store = new FakeAnalysisStore(4);
        var status = new FakeAnalysisStatusStore();
        var model = new FakeDocumentChangeDraftingModel()
            .Fail(DocumentChangeDraftingFailure.ProviderUnavailable)
            .Fail(DocumentChangeDraftingFailure.ConnectionFailed)
            .Fail(DocumentChangeDraftingFailure.ProviderUnavailable);

        await Worker(store, model, status: status).ExecuteAsync(once: true);

        Assert.Equal(3, model.Requests.Count);
        Assert.Equal([State.RetryWaiting, State.RetryWaiting, State.Pending, State.Pending],
            store.Entries.Select(entry => entry.Status));
        Assert.Equal(0, store.Entries[2].Attempts);
        Assert.Equal(("providerOutage", (TimeSpan?)DocumentChangeAnalysisPolicy.InitialOutagePause), Assert.Single(status.Pauses));
    }

    [Fact]
    public async Task AnExhaustedDailyBudgetStopsThePassBeforeAnyEvidenceRead()
    {
        var store = new FakeAnalysisStore(3) { Charged = 99_000 };
        var model = new FakeDocumentChangeDraftingModel();
        var reads = 0;
        store.ReadEvidence = id => { reads++; return Task.FromResult<DocumentChangeAnalysisEvidence?>(store.Evidence[id]); };

        await Worker(store, model, new DocumentChangeAnalysisSettings(100_000, 50_000, 1)).ExecuteAsync(once: true);

        Assert.Equal(0, reads);
        Assert.Empty(model.Requests);
        Assert.Equal([State.WaitingForBudget, State.Pending, State.Pending], store.Entries.Select(entry => entry.Status));
    }

    [Fact]
    public async Task ContinuousModeDrainsThenWaitsForTheWakeup()
    {
        var store = new FakeAnalysisStore(1);
        var model = new FakeDocumentChangeDraftingModel().Draft(FakeDocumentChangeDraftingModel.Output(("h1", "after", "Item 0 new")));
        using var stop = new CancellationTokenSource();
        var wakeup = new FakeWakeup(() => stop.Cancel());

        var result = await Worker(store, model, wakeup: wakeup).ExecuteAsync(once: false, stop.Token);

        Assert.Equal((1, 1), (wakeup.Connects, wakeup.Waits));
        Assert.Equal(1, result.ProgressCount);
        Assert.Equal(State.Succeeded, store.Entries.Single().Status);
    }

    [Fact]
    public async Task ABilledResponseThatCannotBeReadFailsTheEntryChargesItAndIsNoOutage()
    {
        var store = new FakeAnalysisStore(3);
        var status = new FakeAnalysisStatusStore();
        var model = new FakeDocumentChangeDraftingModel();
        for (var call = 0; call < 3; call++)
            model.Reply((_, _) => throw new DocumentChangeDraftingException(DocumentChangeDraftingFailure.InvalidResponse));

        await Worker(store, model, status: status).ExecuteAsync(once: true);

        Assert.All(store.Entries, entry => Assert.Equal((State.Failed, "invalidResponse"), (entry.Status, entry.ErrorCode)));
        Assert.Empty(status.Pauses);
        Assert.Equal(model.Requests.Sum(request => request.ReservedTokens), store.Charged);
    }

    [Fact]
    public async Task TheFirstSuccessAfterAnOutagePauseClearsItAndResetsTheEscalation()
    {
        var store = new FakeAnalysisStore(6);
        var status = new FakeAnalysisStatusStore();
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var model = new FakeDocumentChangeDraftingModel();
        for (var call = 0; call < 3; call++) model.Fail(DocumentChangeDraftingFailure.ProviderUnavailable);
        model.Draft(FakeDocumentChangeDraftingModel.Output(("h1", "after", "Item 2 new")));
        for (var call = 0; call < 3; call++) model.Fail(DocumentChangeDraftingFailure.ConnectionFailed);
        var worker = Worker(store, model, status: status, clock: clock);

        await worker.ExecuteAsync(once: true);
        await worker.ExecuteAsync(once: true);
        Assert.Equal(3, model.Requests.Count);
        clock.Now += DocumentChangeAnalysisPolicy.InitialOutagePause;
        await worker.ExecuteAsync(once: true);

        Assert.Equal(7, model.Requests.Count);
        Assert.Equal(1, status.OutageClears);
        Assert.Equal([DocumentChangeAnalysisPolicy.InitialOutagePause, DocumentChangeAnalysisPolicy.InitialOutagePause],
            status.Pauses.Select(pause => pause.Duration!.Value));
    }

    [Fact]
    public async Task AResumedRetryThatNoLongerFitsKeepsThePreviousRejectionCode()
    {
        var store = new FakeAnalysisStore(1);
        store.ResumeAfter(AnalysisRunOutcome.OutputRejected);
        var model = new FakeDocumentChangeDraftingModel();
        var minimum = DocumentChangeDraftingPrompt.MinimumReservation;

        await Worker(store, model, new DocumentChangeAnalysisSettings(minimum, minimum, 1)).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Failed, "outputRejected"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    private static DocumentChangeAnalysisWorker Worker(FakeAnalysisStore store, FakeDocumentChangeDraftingModel model,
        DocumentChangeAnalysisSettings? settings = null, IWorkerWakeup? wakeup = null, FakeAnalysisStatusStore? status = null,
        FakeClock? clock = null) =>
        new(store, status ?? new FakeAnalysisStatusStore(), model, settings ?? new DocumentChangeAnalysisSettings(10_000_000, concurrency: 1),
            Catalog, clock ?? new FakeClock(DateTimeOffset.UnixEpoch), wakeup);

    private sealed class FakeWakeup(Action onWait) : IWorkerWakeup
    {
        public int Connects { get; private set; }
        public int Waits { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Connects++;
            return Task.CompletedTask;
        }

        public Task WaitAsync(CancellationToken cancellationToken)
        {
            Waits++;
            onWait();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
