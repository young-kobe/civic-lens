using CivicLens.Application.Collection.Processing;

namespace CivicLens.Application.Collection.Jobs;

/// <summary>Drains durable collection and evidence work, then waits for a database wakeup.</summary>
public sealed class CollectionJobWorker(ICollectionWorkerQueue queue, RunCollectionJob runner,
    EvidenceProcessingWorker? evidenceProcessor = null, ICollectionPipelineWakeup? wakeup = null,
    ICollectionScheduleStore? schedules = null)
{
    public const int DefaultBatchSize = 20;
    public const int MaximumBatchSize = 100;
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(2);

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

        var wakeupFailures = once ? 0 : await ConnectUntilAvailableAsync(wakeup!, cancellationToken);
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
                wakeupFailures += await WaitForWorkAsync(wakeup!,
                    drain.QueueFailures > 0 || drain.JobFailures > 0, cancellationToken);
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
                catch (Exception exception) when (IsRecoverable(exception))
                {
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
                    page = await queue.GetEligibleJobIdsAsync(cursor, batchSize, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    queueFailures++;
                    queueFailed = true;
                    break;
                }

                foreach (var jobId in page.JobIds)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    visited++;
                    try
                    {
                        var result = await runner.ExecuteAsync(jobId, artifactDirectory, lease, cancellationToken);
                        if (IsProgress(result.Status))
                            progress++;
                        if (IsFailure(result)) failures++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                    catch (Exception exception) when (IsRecoverable(exception)) { failures++; }
                }

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

    private async Task<EvidenceProcessingWorkerResult> ProcessEvidenceAsync(string artifactDirectory, int batchSize,
        TimeSpan lease, CancellationToken cancellationToken)
    {
        try
        {
            return await evidenceProcessor!.ExecuteAsync(artifactDirectory, batchSize, lease, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new(0, 0); }
        catch (Exception exception) when (IsRecoverable(exception)) { return new(0, 1); }
    }

    private static async Task<int> WaitForWorkAsync(ICollectionPipelineWakeup wakeup, bool reconnect,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        if (!reconnect)
        {
            try
            {
                await wakeup.WaitAsync(cancellationToken);
                return failures;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                failures++;
            }
        }

        await Task.Delay(FailureBackoff, cancellationToken);
        return failures + await ConnectUntilAvailableAsync(wakeup, cancellationToken);
    }

    private static async Task<int> ConnectUntilAvailableAsync(ICollectionPipelineWakeup wakeup,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                await wakeup.ConnectAsync(cancellationToken);
                return failures;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                failures++;
                await Task.Delay(FailureBackoff, cancellationToken);
            }
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

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException;
}
