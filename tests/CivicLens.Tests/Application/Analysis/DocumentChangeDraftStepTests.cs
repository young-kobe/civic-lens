using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftStepTests
{
    [Fact]
    public async Task OnlyTheOfficialsThatTheSourceCoversOnTheLaterObservationDateReachThePromptAndTheRunContext()
    {
        var store = new FakeAnalysisStore(1);
        var id = store.Evidence.Keys.Single();
        var evidence = store.Evidence[id];
        store.Evidence[id] = evidence with
        {
            ConfigurationRevision = StatementDraftFixtures.SpeakerRevision(warrenEndsBefore: new DateOnly(2026, 8, 16)),
            Comparison = evidence.Comparison with { AfterObservedAtUtc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero) }
        };
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "Item 0 new")));

        await Worker(store, model).ExecuteAsync(once: true);

        var request = Assert.Single(model.Requests);
        Assert.Contains("markey", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("warren", request.UserPrompt, StringComparison.Ordinal);
        var context = Assert.Single(store.Runs).ContextJson!;
        Assert.Contains("markey", context, StringComparison.Ordinal);
        Assert.Contains("warren", context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnexpectedModelErrorAfterTheReservationFailsTheEntryAndKeepsTheCharge()
    {
        var store = new FakeAnalysisStore(1);
        var model = new FakeDocumentChangeDraftModel().Reply((_, _) => throw new InvalidOperationException("Broken."));

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
        var model = new FakeDocumentChangeDraftModel();

        await Worker(store, model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Empty(store.Runs);
        Assert.Equal((expected, error.Name), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    [Theory]
    [InlineData(AnalysisModelFailure.AuthenticationFailed, "authenticationFailed")]
    [InlineData(AnalysisModelFailure.BillingFailed, "billingFailed")]
    [InlineData(AnalysisModelFailure.ModelNotFound, "modelNotFound")]
    public async Task AnAccountErrorPausesDraftingUntilRestartWithoutCostingTheEntryAnAttempt(AnalysisModelFailure failure,
        string reason)
    {
        var store = new FakeAnalysisStore(2);
        var status = new FakeAnalysisStatusStore();
        var model = new FakeDocumentChangeDraftModel().Fail(failure);

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
        var model = new FakeDocumentChangeDraftModel()
            .Fail(AnalysisModelFailure.ProviderUnavailable)
            .Fail(AnalysisModelFailure.ConnectionFailed)
            .Fail(AnalysisModelFailure.ProviderUnavailable);

        await Worker(store, model, status: status).ExecuteAsync(once: true);

        Assert.Equal(3, model.Requests.Count);
        Assert.Equal([State.RetryWaiting, State.RetryWaiting, State.Pending, State.Pending],
            store.Entries.Select(entry => entry.Status));
        Assert.Equal(0, store.Entries[2].Attempts);
        Assert.Equal(("providerOutage", (TimeSpan?)AnalysisPolicy.InitialOutagePause), Assert.Single(status.Pauses));
    }

    [Fact]
    public async Task AnExhaustedDailyBudgetStopsThePassBeforeAnyEvidenceRead()
    {
        var store = new FakeAnalysisStore(3) { Charged = 99_000 };
        var model = new FakeDocumentChangeDraftModel();
        var reads = 0;
        store.ReadEvidence = id => { reads++; return Task.FromResult<DocumentChangeDraftEvidence?>(store.Evidence[id]); };

        await Worker(store, model, new DocumentChangeDraftSettings(100_000, 1)).ExecuteAsync(once: true);

        Assert.Equal(0, reads);
        Assert.Empty(model.Requests);
        Assert.Equal([State.WaitingForBudget, State.Pending, State.Pending], store.Entries.Select(entry => entry.Status));
    }

    [Fact]
    public async Task ContinuousModeDrainsThenWaitsForTheWakeup()
    {
        var store = new FakeAnalysisStore(1);
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "Item 0 new")));
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
        var model = new FakeDocumentChangeDraftModel();
        for (var call = 0; call < 3; call++)
            model.Reply((_, _) => throw new AnalysisModelException(AnalysisModelFailure.InvalidResponse));

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
        var model = new FakeDocumentChangeDraftModel();
        for (var call = 0; call < 3; call++) model.Fail(AnalysisModelFailure.ProviderUnavailable);
        model.Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "Item 2 new")));
        for (var call = 0; call < 3; call++) model.Fail(AnalysisModelFailure.ConnectionFailed);
        var worker = Worker(store, model, status: status, clock: clock);

        await worker.ExecuteAsync(once: true);
        await worker.ExecuteAsync(once: true);
        Assert.Equal(3, model.Requests.Count);
        clock.Now += AnalysisPolicy.InitialOutagePause;
        await worker.ExecuteAsync(once: true);

        Assert.Equal(7, model.Requests.Count);
        Assert.Equal(1, status.OutageClears);
        Assert.Equal([AnalysisPolicy.InitialOutagePause, AnalysisPolicy.InitialOutagePause],
            status.Pauses.Select(pause => pause.Duration!.Value));
    }

    [Fact]
    public async Task AResumedRetryThatNoLongerFitsKeepsThePreviousRejectionCode()
    {
        var store = new FakeAnalysisStore(1);
        store.ResumeAfter(AnalysisRunOutcome.OutputRejected);
        var model = new FakeDocumentChangeDraftModel();
        var minimum = DocumentChangeDraftPrompt.MinimumReservation;

        await Worker(store, model, new DocumentChangeDraftSettings(minimum, 1)).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal((State.Failed, "outputRejected"), (store.Entries.Single().Status, store.Entries.Single().ErrorCode));
    }

    private static AnalysisWorker<AnalysisRun> Worker(FakeAnalysisStore store, FakeDocumentChangeDraftModel model,
        DocumentChangeDraftSettings? settings = null, IWorkerWakeup? wakeup = null, FakeAnalysisStatusStore? status = null,
        FakeClock? clock = null) =>
        AnalysisWorkers.Drafting(store, status ?? new FakeAnalysisStatusStore(), model,
            settings ?? new DocumentChangeDraftSettings(10_000_000, concurrency: 1), [],
            clock ?? new FakeClock(DateTimeOffset.UnixEpoch), wakeup);

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
