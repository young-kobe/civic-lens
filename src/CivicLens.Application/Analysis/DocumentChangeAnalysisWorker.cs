using System.Collections.Immutable;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using Codes = CivicLens.Application.Analysis.DocumentChangeAnalysisErrorCodes;
using Failure = CivicLens.Application.Analysis.DocumentChangeDraftingFailure;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Application.Analysis;

public sealed class DocumentChangeAnalysisWorker(IDocumentChangeAnalysisStore store, IDocumentChangeAnalysisStatusStore status,
    IDocumentChangeDraftingModel model, DocumentChangeAnalysisSettings settings, DocumentChangeAnalysisCatalog catalog,
    TimeProvider clock, IWorkerWakeup? wakeup = null)
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);
    private readonly string catalogJson = DocumentChangeDraftingPrompt.SerializeCatalog(catalog);
    private readonly Lock pauseGate = new();
    private int consecutiveOutages;
    private int outagePauses;
    private bool pausedUntilRestart;
    private DateTimeOffset? pausedUntil;
    private int budgetExhausted;

    public async Task<DocumentChangeAnalysisWorkerResult> ExecuteAsync(bool once, CancellationToken cancellationToken = default)
    {
        if (!once && wakeup is null) throw new InvalidOperationException("Continuous drafting requires a durable wakeup provider.");
        _ = await store.ReleaseBudgetWaitsAsync(cancellationToken);
        var failures = once ? 0 : await WorkerLoop.ConnectUntilAvailableAsync(wakeup!, cancellationToken);
        var passes = 0;
        var progress = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (IsPaused(out var resumeAt))
                {
                    if (once) break;
                    await Task.Delay(resumeAt is { } at ? Max(at - clock.GetUtcNow(), TimeSpan.Zero) : Timeout.InfiniteTimeSpan,
                        clock, cancellationToken);
                    continue;
                }
                var drain = await DrainAsync(cancellationToken);
                passes += drain.Passes;
                progress += drain.ProgressCount;
                failures += drain.Failures;
                if (once) break;
                if (IsPaused(out _)) continue;
                failures += await WorkerLoop.WaitForWorkAsync(wakeup!, drain.Failures > 0, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(passes, progress, failures);
    }

    private async Task<DocumentChangeAnalysisWorkerResult> DrainAsync(CancellationToken cancellationToken)
    {
        var passes = 0;
        var total = 0;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested && !IsPaused(out _))
        {
            passes++;
            Interlocked.Exchange(ref budgetExhausted, 0);
            IReadOnlyList<DocumentChangeAnalysisRecord> eligible;
            try { eligible = await store.GetEligibleAsync(settings.Concurrency * 4, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (WorkerLoop.IsRecoverable(exception)) { return new(passes, total, failures + 1); }
            if (eligible.Count == 0) break;
            var progress = 0;
            try
            {
                await Parallel.ForEachAsync(eligible, new ParallelOptions
                {
                    MaxDegreeOfParallelism = settings.Concurrency,
                    CancellationToken = cancellationToken
                }, async (record, token) =>
                {
                    if (Volatile.Read(ref budgetExhausted) == 1 || IsPaused(out _)) return;
                    try
                    {
                        if (await ProcessAsync(record, token)) Interlocked.Increment(ref progress);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception) when (WorkerLoop.IsRecoverable(exception)) { Interlocked.Increment(ref failures); }
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            total += progress;
            if (progress == 0 || Volatile.Read(ref budgetExhausted) == 1) break;
        }
        return new(passes, total, failures);
    }

    private async Task<bool> ProcessAsync(DocumentChangeAnalysisRecord record, CancellationToken cancellationToken)
    {
        var claim = await store.TryClaimAsync(record.ComparisonId, DocumentChangeAnalysisPolicy.LeaseDuration, cancellationToken);
        if (claim is null) return false;
        using var ownership = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = WorkerLoop.RenewLeaseAsync(token => store.RenewAsync(claim, DocumentChangeAnalysisPolicy.LeaseDuration, token),
            DocumentChangeAnalysisPolicy.LeaseDuration, ownership);
        var pass = new Pass(claim.Record.ComparisonId, catalogJson, clock);
        try
        {
            return await DraftAsync(claim, pass, ownership.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var release = new CancellationTokenSource(ReleaseTimeout);
            try { _ = await store.ReleaseAsync(claim, release.Token); }
            catch (Exception exception) when (WorkerLoop.IsRecoverable(exception)) { }
            throw;
        }
        catch (OperationCanceledException) when (ownership.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
        {
            using var timeout = new CancellationTokenSource(ReleaseTimeout);
            return await FailUnexpectedAsync(claim, pass, exception, timeout.Token);
        }
        finally
        {
            ownership.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task<bool> DraftAsync(DocumentChangeAnalysisClaim claim, Pass pass, CancellationToken cancellationToken)
    {
        if (await store.GetChargedTodayAsync(cancellationToken) + DocumentChangeDraftingPrompt.MinimumReservation > settings.DailyTokenLimit)
            return await WaitForBudgetAsync(claim, cancellationToken);
        var evidence = await store.ReadEvidenceAsync(claim.Record.ComparisonId, cancellationToken);
        if (evidence is null) return await CompleteAsync(claim, State.Blocked, Codes.ComparisonUnavailable, cancellationToken);
        if (evidence.ExistingDraftId is not null) return await CompleteAsync(claim, State.Succeeded, Codes.HumanDraftExists, cancellationToken);

        var number = 1;
        string? previousRunId = null;
        ImmutableArray<string> errors = default;
        var rejectedCode = Codes.InputLimitExceeded;
        if (claim.Record.RetryOfRunId is { } retryOf)
        {
            var previous = await store.GetRunAsync(retryOf, cancellationToken)
                ?? throw new InvalidDataException("The rejected run that a retry continues is missing.");
            (number, previousRunId, errors, rejectedCode) = (2, previous.RunId, previous.ValidationErrors, Codes.ForOutcome(previous.Outcome));
        }
        var request = Bounded(DocumentChangeDraftingPrompt.Build(evidence, catalog, errors));
        if (request is null) return await CompleteAsync(claim, number == 1 ? State.Blocked : State.Failed, rejectedCode, cancellationToken);

        while (true)
        {
            var runId = Guid.NewGuid().ToString("N");
            var reservation = await store.ReserveAsync(claim, runId, request.InputHash, request.ReservedTokens,
                settings.DailyTokenLimit, cancellationToken);
            if (reservation == DocumentChangeAnalysisReservation.WaitingForBudget) return MarkBudgetExhausted();
            if (reservation == DocumentChangeAnalysisReservation.LeaseLost) return false;
            pass.Start(runId, previousRunId, request);
            DocumentChangeDraftingResponse response;
            try { response = await model.DraftAsync(request, cancellationToken); }
            catch (DocumentChangeDraftingException exception) { return await FailCallAsync(claim, pass, exception, cancellationToken); }
            await RecordSuccessfulCallAsync(cancellationToken);

            var outcome = StopOutcome(response);
            var run = pass.Run(outcome, response.Usage, response.Usage.Total, response.StopReason, response.RequestId, response.OutputJson);
            if (outcome != AnalysisRunOutcome.Drafted)
                return await SettleAsync(claim, pass, run, State.Failed, Codes.ForOutcome(outcome), cancellationToken);

            var resolution = DocumentChangeDraftingResolver.Resolve(response.OutputJson!, evidence, catalog,
                Guid.NewGuid().ToString("N"), run.FinishedAtUtc);
            if (resolution.Revision is not null)
                return await SettleAsync(claim, pass, run, State.Succeeded, null, cancellationToken, resolution.Revision);
            var rejected = run with
            {
                Outcome = resolution.HasCitationErrors ? AnalysisRunOutcome.CitationRejected : AnalysisRunOutcome.OutputRejected,
                ValidationErrors = resolution.Errors
            };
            var retry = number < DocumentChangeDraftingTask.MaximumValidationPasses
                ? Bounded(DocumentChangeDraftingPrompt.Build(evidence, catalog, resolution.Errors)) : null;
            if (retry is null) return await SettleAsync(claim, pass, rejected, State.Failed, Codes.ForOutcome(rejected.Outcome), cancellationToken);
            if (!await SettleAsync(claim, pass, rejected, State.Running, null, cancellationToken)) return false;
            (number, previousRunId, request) = (number + 1, runId, retry);
        }
    }

    private async Task<bool> FailCallAsync(DocumentChangeAnalysisClaim claim, Pass pass, DocumentChangeDraftingException exception,
        CancellationToken cancellationToken)
    {
        var outcome = Enum.Parse<AnalysisRunOutcome>(exception.Kind.ToString());
        var charged = exception.Kind switch
        {
            Failure.ConnectionFailed => pass.ReservedTokens,
            Failure.InvalidResponse => exception.Usage?.Total ?? pass.ReservedTokens,
            _ => 0
        };
        var run = pass.Run(outcome, exception.Usage ?? AnalysisTokenUsage.None, charged, null, null, null);
        var code = Codes.ForOutcome(outcome);
        switch (exception.Kind)
        {
            case Failure.AuthenticationFailed or Failure.BillingFailed or Failure.ModelNotFound:
                return await PauseAsync(claim, pass, run, code, null, cancellationToken);
            case Failure.ProviderUnavailable or Failure.ConnectionFailed
                when Interlocked.Increment(ref consecutiveOutages) >= DocumentChangeAnalysisPolicy.OutageFailureBound:
                return await PauseAsync(claim, pass, run, Codes.ProviderOutage,
                    DocumentChangeAnalysisPolicy.OutagePause(Interlocked.Increment(ref outagePauses) - 1), cancellationToken);
            case Failure.ProviderRejected or Failure.InvalidResponse:
                return await SettleAsync(claim, pass, run, State.Failed, code, cancellationToken);
            default:
                return await SettleAsync(claim, pass, run, State.RetryWaiting, code, cancellationToken,
                    retryDelay: DocumentChangeAnalysisPolicy.RetryDelay(claim.Record.Attempts));
        }
    }

    private async Task RecordSuccessfulCallAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref consecutiveOutages, 0);
        if (Interlocked.Exchange(ref outagePauses, 0) > 0) await status.ClearOutagePauseAsync(cancellationToken);
    }

    private async Task<bool> PauseAsync(DocumentChangeAnalysisClaim claim, Pass pass, AnalysisRun run, string reason,
        TimeSpan? duration, CancellationToken cancellationToken)
    {
        lock (pauseGate)
        {
            if (duration is null) pausedUntilRestart = true;
            else pausedUntil = clock.GetUtcNow() + duration.Value;
        }
        Interlocked.Exchange(ref consecutiveOutages, 0);
        var settled = await SettleAsync(claim, pass, run, State.Pending, null, cancellationToken);
        await status.RecordPauseAsync(reason, duration, cancellationToken);
        return settled;
    }

    private async Task<bool> FailUnexpectedAsync(DocumentChangeAnalysisClaim claim, Pass pass, Exception exception,
        CancellationToken cancellationToken)
    {
        var code = WorkerLoop.ErrorCode(exception);
        if (pass.RunId is not null)
            return await SettleAsync(claim, pass, pass.Run(AnalysisRunOutcome.ProcessingFailed, AnalysisTokenUsage.None,
                pass.ReservedTokens, null, null, null), State.Failed, code, cancellationToken);
        if (exception is ArgumentException or InvalidDataException) return await CompleteAsync(claim, State.Blocked, code, cancellationToken);
        return await CompleteAsync(claim, State.RetryWaiting, code, cancellationToken,
            DocumentChangeAnalysisPolicy.RetryDelay(claim.Record.Attempts));
    }

    private async Task<bool> WaitForBudgetAsync(DocumentChangeAnalysisClaim claim, CancellationToken cancellationToken)
    {
        _ = await CompleteAsync(claim, State.WaitingForBudget, Codes.DailyTokenLimit, cancellationToken);
        return MarkBudgetExhausted();
    }

    private bool MarkBudgetExhausted()
    {
        Interlocked.Exchange(ref budgetExhausted, 1);
        return true;
    }

    private DocumentChangeDraftingRequest? Bounded(DocumentChangeDraftingRequest? request) =>
        request is not null && request.ReservedTokens <= settings.RunTokenLimit ? request : null;

    private static AnalysisRunOutcome StopOutcome(DocumentChangeDraftingResponse response) => response.StopReason switch
    {
        "refusal" => AnalysisRunOutcome.Refused,
        "max_tokens" => AnalysisRunOutcome.OutputLimitReached,
        "end_turn" when response.OutputJson is not null => AnalysisRunOutcome.Drafted,
        _ => AnalysisRunOutcome.UnexpectedStop
    };

    private async Task<bool> SettleAsync(DocumentChangeAnalysisClaim claim, Pass pass, AnalysisRun run, State state,
        string? errorCode, CancellationToken cancellationToken, DocumentChangeDraftRevision? revision = null, TimeSpan? retryDelay = null)
    {
        var settled = await store.CheckpointAsync(DocumentChangeAnalysisCheckpoint.From(claim, state, errorCode, retryDelay, revision),
            run, cancellationToken);
        pass.Clear();
        return settled;
    }

    private Task<bool> CompleteAsync(DocumentChangeAnalysisClaim claim, State state, string errorCode, CancellationToken cancellationToken,
        TimeSpan? retryDelay = null) =>
        store.CheckpointAsync(DocumentChangeAnalysisCheckpoint.From(claim, state, errorCode, retryDelay), null, cancellationToken);

    private bool IsPaused(out DateTimeOffset? resumeAt)
    {
        lock (pauseGate)
        {
            if (pausedUntil <= clock.GetUtcNow()) pausedUntil = null;
            resumeAt = pausedUntil;
            return pausedUntilRestart || pausedUntil is not null;
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private sealed class Pass(string comparisonId, string contextJson, TimeProvider clock)
    {
        private DocumentChangeDraftingRequest? request;
        private string? previousRunId;
        private DateTimeOffset started;

        public string? RunId { get; private set; }
        public long ReservedTokens => request?.ReservedTokens ?? 0;

        public void Start(string runId, string? previous, DocumentChangeDraftingRequest pending) =>
            (RunId, previousRunId, request, started) = (runId, previous, pending, clock.GetUtcNow());

        public void Clear() => (RunId, request) = (null, null);

        public AnalysisRun Run(AnalysisRunOutcome outcome, AnalysisTokenUsage usage, long charged, string? stopReason, string? requestId,
            string? outputJson) =>
            new(RunId!, comparisonId, DocumentChangeDraftingTask.Task, DocumentChangeDraftingTask.TaskVersion, request!.Model,
                DocumentChangeDraftingTask.PromptVersion, DocumentChangeDraftingTask.SchemaVersion, request.InputHash, previousRunId,
                outcome, usage, Math.Max(charged, usage.Total), stopReason, requestId, outputJson, [], contextJson, started,
                clock.GetUtcNow());
    }
}
