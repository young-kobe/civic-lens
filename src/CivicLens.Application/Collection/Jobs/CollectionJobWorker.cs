namespace CivicLens.Application.Collection.Jobs;

/// <summary>Polls a bounded durable queue and runs its jobs sequentially through the fenced job runner.</summary>
public sealed class CollectionJobWorker(ICollectionWorkerQueue queue, RunCollectionJob runner)
{
    public const int DefaultBatchSize = 20;
    public const int MaximumBatchSize = 100;
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(60);

    public async Task<CollectionJobWorkerResult> ExecuteAsync(string artifactDirectory, bool once,
        int batchSize = DefaultBatchSize, TimeSpan? pollInterval = null, TimeSpan? leaseDuration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        if (batchSize is < 1 or > MaximumBatchSize) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var interval = pollInterval ?? DefaultPollInterval;
        if (interval < TimeSpan.FromMilliseconds(250) || interval > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be from 250 milliseconds to 60 seconds.");
        var lease = leaseDuration ?? DefaultLeaseDuration;
        if (lease < TimeSpan.FromMilliseconds(30))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be at least 30 milliseconds.");

        var passes = 0;
        var visited = 0;
        var jobFailures = 0;
        var queueFailures = 0;
        CollectionWorkerCursor? cursor = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            passes++;
            CollectionWorkerQueuePage page;
            try
            {
                page = await queue.GetEligibleJobIdsAsync(cursor, batchSize, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                queueFailures++;
                if (once) break;
                await DelayAsync(interval, cancellationToken);
                continue;
            }

            foreach (var jobId in page.JobIds)
            {
                if (cancellationToken.IsCancellationRequested) break;
                visited++;
                try
                {
                    var result = await runner.ExecuteAsync(jobId, artifactDirectory, lease, cancellationToken);
                    if ((result.Status is CollectionJobRunStatus.AttemptFailed or CollectionJobRunStatus.ImportPending or
                         CollectionJobRunStatus.RecoveryBlocked or CollectionJobRunStatus.LostOwnership) ||
                        result.Job?.State == CollectionJobState.Failed)
                        jobFailures++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    jobFailures++;
                }
            }

            if (once) break;
            cursor = page.NextCursor;
            await DelayAsync(interval, cancellationToken);
        }

        return new(passes, visited, jobFailures, queueFailures);
    }

    private static async Task DelayAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try { await Task.Delay(interval, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not StackOverflowException;
}
