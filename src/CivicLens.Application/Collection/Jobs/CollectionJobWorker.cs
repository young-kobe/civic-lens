using CivicLens.Application.Collection.Processing;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Collection.Jobs;

/// <summary>Drains durable collection and evidence work, then waits for a database wakeup.</summary>
public sealed class CollectionJobWorker(ICollectionWorkerQueue queue, RunCollectionJob runner, ILogger<CollectionJobWorker> logger,
    WorkerBeat beat, EvidenceProcessingWorker? evidenceProcessor = null, IWorkerWakeup? wakeup = null,
    ICollectionScheduleStore? schedules = null, int concurrency = CollectionLanePolicy.DefaultConcurrency)
{
    public const int DefaultBatchSize = 20;
    public const int MaximumBatchSize = 100;
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(60);

    private readonly int lanes = CollectionLanePolicy.ValidateConcurrency(concurrency);

    public async Task<CollectionJobWorkerResult> ExecuteAsync(string artifactDirectory, bool once,
        int batchSize = DefaultBatchSize, TimeSpan? leaseDuration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        if (batchSize is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (!once && wakeup is null)
            throw new InvalidOperationException("Continuous pipeline work requires a durable wakeup provider.");
        var lease = leaseDuration ?? DefaultLeaseDuration;
        if (evidenceProcessor is not null)
            EvidenceProcessingPolicy.ValidateLeaseDuration(lease);
        if (lease < TimeSpan.FromMilliseconds(30))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be at least 30 milliseconds.");

        await beat.BeatAsync(cancellationToken);
        var wakeupFailures = once ? 0 : await WorkerLoop.ConnectUntilAvailableAsync(wakeup!, cancellationToken);
        var passes = 0;
        var visited = 0;
        var jobFailures = 0;
        var queueFailures = 0;
        do
        {
            var drain = await DrainAsync(artifactDirectory, batchSize, lease, cancellationToken);
            passes += drain.Passes;
            visited += drain.JobsVisited;
            jobFailures += drain.JobFailures;
            queueFailures += drain.QueueFailures;
            if (once || cancellationToken.IsCancellationRequested) break;
            try
            {
                wakeupFailures += await WorkerLoop.WaitForWorkAsync(wakeup!,
                    drain.QueueFailures > 0 || drain.JobFailures > 0, beat, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
        while (!cancellationToken.IsCancellationRequested);

        return new(passes, visited, jobFailures, queueFailures + wakeupFailures);
    }

    private async Task<CollectionJobWorkerResult> DrainAsync(string artifactDirectory, int batchSize,
        TimeSpan lease, CancellationToken cancellationToken)
    {
        var passes = 0;
        var visited = 0;
        var failures = 0;
        var queueFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            passes++;
            await beat.BeatIfDueAsync(cancellationToken);
            var progress = 0;
            var scheduleFailed = false;
            if (schedules is not null)
            {
                try
                {
                    var advanced = await schedules.AdvanceDueAsync(batchSize, cancellationToken);
                    if (advanced is < 0 || advanced > batchSize)
                        throw new InvalidOperationException("Schedule store exceeded the requested admission bound.");
                    progress += advanced;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
                {
                    logger.LogError(exception, "The collection schedule could not advance.");
                    failures++;
                    scheduleFailed = true;
                }
            }
            CollectionWorkerCursor? cursor = null;
            var queueFailed = false;
            do
            {
                CollectionWorkerQueuePage page;
                try
                {
                    page = await queue.GetEligibleJobsAsync(cursor, batchSize, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
                {
                    logger.LogError(exception, "The collection queue could not list eligible jobs.");
                    queueFailures++;
                    queueFailed = true;
                    break;
                }

                var pageResult = await RunPageAsync(page, artifactDirectory, lease, cancellationToken);
                visited += pageResult.Visited;
                progress += pageResult.Progress;
                failures += pageResult.Failures;

                if (evidenceProcessor is not null && !cancellationToken.IsCancellationRequested)
                {
                    var result = await ProcessEvidenceAsync(artifactDirectory, batchSize, lease, cancellationToken);
                    progress += result.ProgressCount;
                    failures += result.Failures;
                }

                cursor = page.NextCursor;
            }
            while (cursor is not null && !cancellationToken.IsCancellationRequested);

            if (queueFailed && evidenceProcessor is not null && !cancellationToken.IsCancellationRequested)
            {
                var result = await ProcessEvidenceAsync(artifactDirectory, batchSize, lease, cancellationToken);
                progress += result.ProgressCount;
                failures += result.Failures;
            }

            if (queueFailed || scheduleFailed || progress == 0 || cancellationToken.IsCancellationRequested) break;
        }

        return new(passes, visited, failures, queueFailures);
    }

    private async Task<PageResult> RunPageAsync(CollectionWorkerQueuePage page, string artifactDirectory,
        TimeSpan lease, CancellationToken cancellationToken)
    {
        var visited = 0;
        var progress = 0;
        var failures = 0;
        try
        {
            await Parallel.ForEachAsync(LanesOf(page), new ParallelOptions
            {
                MaxDegreeOfParallelism = lanes,
                CancellationToken = cancellationToken
            }, async (laneJobs, token) =>
            {
                foreach (var job in laneJobs)
                {
                    if (token.IsCancellationRequested) return;
                    Interlocked.Increment(ref visited);
                    try
                    {
                        var result = await runner.ExecuteAsync(job.JobId, artifactDirectory, lease, token);
                        if (IsProgress(result.Status)) Interlocked.Increment(ref progress);
                        if (IsFailure(result)) Interlocked.Increment(ref failures);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
                    {
                        logger.LogError(exception, "Collection job {JobId} failed.", job.JobId);
                        Interlocked.Increment(ref failures);
                    }
                    await beat.BeatIfDueAsync(token);
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(visited, progress, failures);
    }

    /// <summary>One lane holds every job of a pacing key, in queue order. One lane holds the whole page when only one job may run.</summary>
    private IEnumerable<IReadOnlyList<CollectionWorkerQueueJob>> LanesOf(CollectionWorkerQueuePage page) =>
        lanes == 1
            ? [page.Jobs]
            : page.Jobs.GroupBy(job => job.PacingKey, StringComparer.Ordinal).Select(group => group.ToArray());

    private readonly record struct PageResult(int Visited, int Progress, int Failures);

    private async Task<EvidenceProcessingWorkerResult> ProcessEvidenceAsync(string artifactDirectory, int batchSize,
        TimeSpan lease, CancellationToken cancellationToken)
    {
        try
        {
            return await evidenceProcessor!.ExecuteAsync(artifactDirectory, batchSize, lease, beat, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(0, 0); }
        catch (Exception exception) when (WorkerLoop.IsRecoverable(exception))
        {
            logger.LogError(exception, "Evidence processing failed.");
            return new(0, 1);
        }
    }

    private static bool IsFailure(CollectionJobRunResult result) =>
        result.Status is CollectionJobRunStatus.AttemptFailed or CollectionJobRunStatus.ImportPending or
            CollectionJobRunStatus.RecoveryBlocked ||
        result.Job?.State == CollectionJobState.Failed;

    private static bool IsProgress(CollectionJobRunStatus status) => status is
        CollectionJobRunStatus.Completed or CollectionJobRunStatus.AttemptFailed or
        CollectionJobRunStatus.Reconciled or CollectionJobRunStatus.Exhausted or
        CollectionJobRunStatus.Cancelled;
}
