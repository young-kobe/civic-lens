using CivicLens.Core.Analysis;
using Microsoft.Extensions.Logging;
using Codes = CivicLens.Application.Analysis.AnalysisErrorCodes;
using Failure = CivicLens.Application.Analysis.AnalysisModelFailure;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Application.Analysis;

public sealed class AnalysisWorker<TRun>(IAnalysisTaskStep<TRun> step, IAnalysisStatusStore status, TimeProvider clock,
    ILogger<AnalysisWorker<TRun>> logger,
    WorkerBeat beat, IWorkerWakeup? wakeup = null, Func<CancellationToken, Task>? beforeEachPass = null) where TRun : class
{
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);
    private readonly IAnalysisTaskStep<TRun> step = step;
    private readonly IAnalysisQueue<TRun> queue = step.Queue;
    private readonly AnalysisSettings settings = step.Settings;
    private readonly IAnalysisStatusStore status = status;
    private readonly TimeProvider clock = clock;
    private readonly AnalysisPauseState pause = new(clock);
    private int budgetExhausted;

    public async Task<AnalysisWorkerResult> ExecuteAsync(bool once, CancellationToken cancellationToken = default)
    {
        if (!once && wakeup is null) throw new InvalidOperationException("Continuous analysis requires a durable wakeup provider.");
        await beat.BeatAsync(cancellationToken);
        _ = await queue.ReleaseBudgetWaitsAsync(cancellationToken);
        var failures = once ? 0 : await WorkerLoop.ConnectUntilAvailableAsync(wakeup!, cancellationToken);
        var passes = 0;
        var progress = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (pause.IsPaused(out var resumeAt))
                {
                    if (once) break;
                    var pauseLength = resumeAt is { } at ? Max(at - clock.GetUtcNow(), TimeSpan.Zero) : Timeout.InfiniteTimeSpan;
                    Task Pause(CancellationToken token) => Task.Delay(pauseLength, clock, token);
                    await beat.WaitAsync(Pause, cancellationToken);
                    continue;
                }
                var drain = await DrainAsync(cancellationToken);
                passes += drain.Passes;
                progress += drain.ProgressCount;
                failures += drain.Failures;
                if (once) break;
                if (pause.IsPaused(out _)) continue;
                failures += await WorkerLoop.WaitForWorkAsync(wakeup!, drain.Failures > 0, beat, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(passes, progress, failures);
    }

    private async Task<AnalysisWorkerResult> DrainAsync(CancellationToken cancellationToken)
    {
        var passes = 0;
        var total = 0;
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested && !pause.IsPaused(out _))
        {
            passes++;
            await beat.BeatIfDueAsync(cancellationToken);
            Interlocked.Exchange(ref budgetExhausted, 0);
            IReadOnlyList<AnalysisRecord> eligible;
            try
            {
                if (beforeEachPass is not null) await beforeEachPass(cancellationToken);
                eligible = await queue.GetEligibleAsync(settings.Concurrency * 4, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
            {
                logger.LogError(exception, "Analysis {Task} could not list eligible work.", step.Identity.Task);
                return new(passes, total, failures + 1);
            }
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
                    if (Volatile.Read(ref budgetExhausted) == 1 || pause.IsPaused(out _)) return;
                    try
                    {
                        if (await ProcessAsync(record, token)) Interlocked.Increment(ref progress);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
                    {
                        logger.LogError(exception, "Analysis {Task} failed for subject {SubjectId}.", step.Identity.Task, record.SubjectId);
                        Interlocked.Increment(ref failures);
                    }
                    await beat.BeatIfDueAsync(token);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            total += progress;
            if (progress == 0 || Volatile.Read(ref budgetExhausted) == 1) break;
        }
        return new(passes, total, failures);
    }

    private async Task<bool> ProcessAsync(AnalysisRecord record, CancellationToken cancellationToken)
    {
        var claim = await queue.TryClaimAsync(record.SubjectId, AnalysisPolicy.LeaseDuration, cancellationToken);
        if (claim is null) return false;
        using var ownership = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = WorkerLoop.RenewLeaseAsync(token => queue.RenewAsync(claim, AnalysisPolicy.LeaseDuration, token),
            AnalysisPolicy.LeaseDuration, ownership);
        var attempt = new Attempt(this, claim);
        try
        {
            return await RunStepAsync(attempt, ownership.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var release = new CancellationTokenSource(ReleaseTimeout);
            try { _ = await queue.ReleaseAsync(claim, release.Token); }
            catch (Exception exception) when (WorkerLoop.IsRecoverable(exception)) { }
            throw;
        }
        catch (OperationCanceledException) when (ownership.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
        {
            logger.LogError(exception, "Analysis {Task} hit an unexpected failure for subject {SubjectId}.", step.Identity.Task, record.SubjectId);
            using var timeout = new CancellationTokenSource(ReleaseTimeout);
            return await attempt.FailUnexpectedAsync(exception, timeout.Token);
        }
        finally
        {
            ownership.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task<bool> RunStepAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        if (await queue.GetChargedTodayAsync(cancellationToken) + settings.MinimumReservation > settings.DailyTokenLimit)
            return await attempt.WaitForBudgetAsync(cancellationToken);
        return await step.RunAsync(attempt, cancellationToken);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    private sealed class Attempt(AnalysisWorker<TRun> worker, AnalysisClaim claim) : IAnalysisAttempt<TRun>
    {
        private AnalysisModelRequest? request;
        private string? previousRunId;
        private DateTimeOffset started = worker.clock.GetUtcNow();

        public AnalysisClaim Claim { get; } = claim;
        public string? ContextJson { get; set; }
        public string? RunId { get; private set; }

        public async Task<AnalysisCall> CallModelAsync(AnalysisModelRequest modelRequest, string? previous,
            Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> send, CancellationToken cancellationToken)
        {
            var runId = Guid.NewGuid().ToString("N");
            var reservation = await worker.queue.ReserveAsync(Claim, runId, modelRequest.InputHash, modelRequest.ReservedTokens,
                worker.settings.DailyTokenLimit, cancellationToken);
            if (reservation == AnalysisReservation.WaitingForBudget) return new(null, MarkBudgetExhausted());
            if (reservation == AnalysisReservation.LeaseLost) return new(null, false);
            (RunId, previousRunId, request, started) = (runId, previous, modelRequest, worker.clock.GetUtcNow());
            AnalysisModelResponse response;
            try { response = await send(modelRequest, cancellationToken); }
            catch (AnalysisModelException exception) { return new(null, await FailCallAsync(exception, cancellationToken)); }
            if (worker.pause.RecordSuccessfulCall()) await worker.status.ClearOutagePauseAsync(cancellationToken);
            return new(response, false);
        }

        public AnalysisRun ResponseRun(AnalysisRunOutcome outcome, AnalysisModelResponse response) =>
            Run(outcome, response.Usage, response.Usage.Total, response.StopReason, response.RequestId, response.OutputJson);

        public AnalysisRun BypassRun(string model, string inputHash, string? previous)
        {
            var identity = worker.step.Identity;
            return new(Guid.NewGuid().ToString("N"), Claim.Record.SubjectId, identity.Task, identity.TaskVersion, model,
                identity.PromptVersion, identity.SchemaVersion, inputHash, previous, AnalysisRunOutcome.Bypassed,
                AnalysisTokenUsage.None, 0, null, null, null, [], ContextJson, started, worker.clock.GetUtcNow());
        }

        public Task<bool> SettleAsync(TRun run, AnalysisState state, string? errorCode, CancellationToken cancellationToken) =>
            SettleAsync(run, AnalysisCheckpoint.From(Claim, state, errorCode), cancellationToken);

        public async Task<bool> SettleAsync(TRun run, AnalysisCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            var settled = await worker.queue.CheckpointAsync(checkpoint, run, cancellationToken);
            (RunId, request) = (null, null);
            return settled;
        }

        public Task<bool> CompleteAsync(AnalysisState state, string errorCode, CancellationToken cancellationToken) =>
            worker.queue.CheckpointAsync(AnalysisCheckpoint.From(Claim, state, errorCode), null, cancellationToken);

        public async Task<bool> WaitForBudgetAsync(CancellationToken cancellationToken)
        {
            _ = await CompleteAsync(State.WaitingForBudget, Codes.DailyTokenLimit, cancellationToken);
            return MarkBudgetExhausted();
        }

        public async Task<bool> FailUnexpectedAsync(Exception exception, CancellationToken cancellationToken)
        {
            var code = WorkerLoop.ErrorCode(exception);
            if (RunId is not null)
                return await SettleAsync(worker.step.Wrap(Run(AnalysisRunOutcome.ProcessingFailed, AnalysisTokenUsage.None,
                    ReservedTokens, null, null, null)), State.Failed, code, cancellationToken);
            if (exception is ArgumentException or InvalidDataException) return await CompleteAsync(State.Blocked, code, cancellationToken);
            return await RetryLaterAsync(code, cancellationToken);
        }

        private long ReservedTokens => request?.ReservedTokens ?? 0;

        private async Task<bool> FailCallAsync(AnalysisModelException exception, CancellationToken cancellationToken)
        {
            var outcome = Enum.Parse<AnalysisRunOutcome>(exception.Kind.ToString());
            var charged = exception.Kind switch
            {
                Failure.ConnectionFailed => ReservedTokens,
                Failure.InvalidResponse => exception.Usage?.Total ?? ReservedTokens,
                Failure.RateLimited or Failure.ProviderUnavailable or Failure.AuthenticationFailed or Failure.BillingFailed or
                    Failure.ModelNotFound or Failure.ProviderRejected => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(exception), exception.Kind, "Model failure has no token charge.")
            };
            var run = worker.step.Wrap(Run(outcome, exception.Usage ?? AnalysisTokenUsage.None, charged, null, null, null));
            var code = Codes.ForOutcome(outcome);
            switch (exception.Kind)
            {
                case Failure.AuthenticationFailed or Failure.BillingFailed or Failure.ModelNotFound:
                    return await PauseAsync(run, code, null, cancellationToken);
                case Failure.ProviderUnavailable or Failure.ConnectionFailed when worker.pause.OutageBoundReached():
                    return await PauseAsync(run, Codes.ProviderOutage, worker.pause.NextOutagePause(), cancellationToken);
                case Failure.ProviderRejected or Failure.InvalidResponse:
                    return await SettleAsync(run, State.Failed, code, cancellationToken);
                default:
                    return await SettleAsync(run, AnalysisCheckpoint.From(Claim, State.RetryWaiting, code,
                        AnalysisPolicy.RetryDelay(Claim.Record.Attempts)), cancellationToken);
            }
        }

        private async Task<bool> PauseAsync(TRun run, string reason, TimeSpan? duration, CancellationToken cancellationToken)
        {
            worker.pause.Pause(duration);
            var settled = await SettleAsync(run, State.Pending, null, cancellationToken);
            await worker.status.RecordPauseAsync(reason, duration, cancellationToken);
            return settled;
        }

        private Task<bool> RetryLaterAsync(string errorCode, CancellationToken cancellationToken) =>
            worker.queue.CheckpointAsync(AnalysisCheckpoint.From(Claim, State.RetryWaiting, errorCode,
                AnalysisPolicy.RetryDelay(Claim.Record.Attempts)), null, cancellationToken);

        private AnalysisRun Run(AnalysisRunOutcome outcome, AnalysisTokenUsage usage, long charged, string? stopReason,
            string? requestId, string? outputJson)
        {
            var identity = worker.step.Identity;
            return new(RunId!, Claim.Record.SubjectId, identity.Task, identity.TaskVersion, request!.Model, identity.PromptVersion,
                identity.SchemaVersion, request.InputHash, previousRunId, outcome, usage, Math.Max(charged, usage.Total), stopReason,
                requestId, outputJson, [], ContextJson, started, worker.clock.GetUtcNow());
        }

        private bool MarkBudgetExhausted()
        {
            Interlocked.Exchange(ref worker.budgetExhausted, 1);
            return true;
        }
    }
}
