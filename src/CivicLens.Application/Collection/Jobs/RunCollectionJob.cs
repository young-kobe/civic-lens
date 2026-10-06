using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Jobs;

/// <summary>Runs at most one new collection attempt and reconciles an interrupted attempt before doing so.</summary>
public sealed class RunCollectionJob(
    ICollectionJobStore jobs,
    ICollectionAttemptStore attempts,
    ICollectionReceiptHandoffStore handoffs,
    ICaptureArtifactVerifier verifier,
    ICollectorProcess collector)
{
    public async Task<CollectionJobRunResult> ExecuteAsync(string jobId, string artifactDirectory,
        TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        if (leaseDuration < TimeSpan.FromMilliseconds(30))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be at least 30 milliseconds.");

        var claim = await jobs.TryClaimAsync(jobId, leaseDuration, cancellationToken);
        if (claim is null) return new CollectionJobRunResult(CollectionJobRunStatus.NotClaimed, null);

        await using var lease = new CollectionJobLeaseGuard(jobs, claim, leaseDuration, cancellationToken);
        try
        {
            var job = claim.Job;
            if (IsTerminal(job.State)) return new CollectionJobRunResult(CollectionJobRunStatus.Reconciled, job);

            var unresolved = job.Attempts.LastOrDefault(item => item.Resolution is null);
            if (unresolved is not null)
            {
                var recovered = await RecoverAsync(lease, unresolved, artifactDirectory);
                if (!recovered)
                    return new CollectionJobRunResult(lease.IsOwned ? CollectionJobRunStatus.RecoveryBlocked : CollectionJobRunStatus.LostOwnership,
                        await ReadJobBoundedAsync(jobId));
                job = await ReadJobBoundedAsync(jobId) ?? job;
                if (job.CancellationRequested || IsTerminal(job.State))
                    return new CollectionJobRunResult(CollectionJobRunStatus.Reconciled, job);
            }

            if (cancellationToken.IsCancellationRequested || lease.CancellationRequested)
                return new CollectionJobRunResult(CollectionJobRunStatus.Cancelled, await ReadJobBoundedAsync(jobId));

            var start = await lease.TryStartAttemptAsync(Path.GetFullPath(artifactDirectory), lease.WorkToken);
            if (start.Status != CollectionJobStartStatus.Started || start.Attempt is null || start.CollectorLease is null)
                return new CollectionJobRunResult(MapStatus(start.Status), await ReadJobBoundedAsync(jobId), start.BlockReason, start.RetryAt);

            return await CollectOneAsync(lease, start.Attempt, artifactDirectory, cancellationToken);
        }
        catch (OperationCanceledException) when (!lease.IsOwned)
        {
            return new CollectionJobRunResult(CollectionJobRunStatus.LostOwnership, await ReadJobBoundedAsync(jobId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CollectionJobRunResult(CollectionJobRunStatus.Cancelled, await ReadJobBoundedAsync(jobId));
        }
        finally
        {
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await lease.ReleaseClaimAsync(releaseTimeout.Token); }
            catch { }
        }
    }

    private async Task<CollectionJobRunResult> CollectOneAsync(CollectionJobLeaseGuard lease,
        CollectionJobAttempt attempt, string artifactDirectory, CancellationToken callerToken)
    {
        CollectionResult? receipt = null;
        Exception? executionFailure = null;
        try
        {
            receipt = await collector.RunAsync(attempt.Request, lease.CollectorToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            executionFailure = exception;
        }
        finally
        {
            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await lease.ReleaseCollectorAsync(releaseTimeout.Token); }
            catch { }
        }

        if (receipt is not null)
        {
            try
            {
                receipt.ValidateAgainst(attempt.Request);
                using var verifyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await verifier.VerifyAsync(attempt.Request, receipt, Path.GetFullPath(artifactDirectory), verifyTimeout.Token);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                executionFailure = exception;
                receipt = null;
            }
        }

        if (receipt is not null)
            return await PreserveAndSettleAsync(lease, attempt, receipt);

        if (!lease.IsOwned)
            return new CollectionJobRunResult(CollectionJobRunStatus.LostOwnership, await ReadJobBoundedAsync(lease.JobId));

        if (lease.CancellationRequested)
        {
            var cancelled = new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null,
                executionFailure?.GetType().Name ?? "CancelledByRequest");
            using var cancelSettleTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var cancelledSettled = await lease.SettleAttemptAsync(attempt.AttemptId, cancelled, cancelSettleTimeout.Token);
            return new CollectionJobRunResult(cancelledSettled ? CollectionJobRunStatus.Cancelled : CollectionJobRunStatus.LostOwnership,
                await ReadJobBoundedAsync(lease.JobId));
        }
        if (callerToken.IsCancellationRequested)
            return new CollectionJobRunResult(CollectionJobRunStatus.Cancelled, await ReadJobBoundedAsync(lease.JobId));
        var interrupted = new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null,
            executionFailure?.GetType().Name ?? "CollectorInterrupted");
        using var settlementTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        bool settled;
        try { settled = await lease.SettleAttemptAsync(attempt.AttemptId, interrupted, settlementTimeout.Token); }
        catch { settled = false; }
        return new CollectionJobRunResult(settled ? CollectionJobRunStatus.AttemptFailed : CollectionJobRunStatus.LostOwnership,
            await ReadJobBoundedAsync(lease.JobId));
    }

    private async Task<CollectionJobRunResult> PreserveAndSettleAsync(CollectionJobLeaseGuard lease,
        CollectionJobAttempt attempt, CollectionResult receipt)
    {
        var handoff = new PendingCollectionHandoff(PendingCollectionHandoff.CurrentVersion, attempt.AttemptId,
            attempt.Request, receipt);
        handoff.Validate();
        var imported = false;
        var settledConfirmed = false;
        using var preservationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await handoffs.SaveAsync(handoff, preservationTimeout.Token);
            await using (await handoffs.AcquireRecoveryLeaseAsync(preservationTimeout.Token))
            {
                if (!lease.IsOwned)
                    return new CollectionJobRunResult(CollectionJobRunStatus.LostOwnership,
                        await ReadJobBoundedAsync(lease.JobId));

                imported = await ImportReceiptAsync(attempt.AttemptId, attempt.Request, receipt, preservationTimeout.Token);
                if (!imported)
                    return new CollectionJobRunResult(CollectionJobRunStatus.ImportPending,
                        await ReadJobBoundedAsync(lease.JobId));

                if (!lease.IsOwned)
                    return new CollectionJobRunResult(CollectionJobRunStatus.LostOwnership,
                        await ReadJobBoundedAsync(lease.JobId));
                var settled = await lease.SettleAttemptAsync(attempt.AttemptId,
                    CollectionJobLifecycle.Resolve(receipt), preservationTimeout.Token);
                if (!settled)
                {
                    if (!lease.IsOwned)
                        return new CollectionJobRunResult(CollectionJobRunStatus.LostOwnership,
                            await ReadJobBoundedAsync(lease.JobId));
                    return new CollectionJobRunResult(CollectionJobRunStatus.ImportPending,
                        await ReadJobBoundedAsync(lease.JobId));
                }
                settledConfirmed = true;

                try { await handoffs.DeleteAsync(attempt.AttemptId, preservationTimeout.Token); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException) { }
            }
        }
        catch (OperationCanceledException) when (preservationTimeout.IsCancellationRequested) { }

        if (!imported)
            return new CollectionJobRunResult(lease.IsOwned ? CollectionJobRunStatus.ImportPending : CollectionJobRunStatus.LostOwnership,
                await ReadJobBoundedAsync(lease.JobId));
        if (!settledConfirmed)
            return new CollectionJobRunResult(lease.IsOwned ? CollectionJobRunStatus.ImportPending : CollectionJobRunStatus.LostOwnership,
                await ReadJobBoundedAsync(lease.JobId));
        return new CollectionJobRunResult(CollectionJobRunStatus.Completed,
            await ReadJobBoundedAsync(lease.JobId));
    }

    private async Task<bool> RecoverAsync(CollectionJobLeaseGuard lease, CollectionJobAttempt attempt,
        string artifactDirectory)
    {
        var cancellationToken = lease.WorkToken;
        var retained = await attempts.GetAsync(attempt.AttemptId, cancellationToken);
        if (retained is not null)
        {
            await using var spoolLease = await handoffs.AcquireRecoveryLeaseAsync(cancellationToken);
            CollectionResult? verifiedReceipt = null;
            var entryIds = await handoffs.ListAsync(cancellationToken);
            if (entryIds.Contains(attempt.AttemptId, StringComparer.Ordinal))
            {
                try
                {
                    var entry = await handoffs.LoadAsync(attempt.AttemptId, cancellationToken);
                    if (entry.Request == attempt.Request)
                    {
                        await verifier.VerifyAsync(entry.Request, entry.Receipt, Path.GetFullPath(artifactDirectory), cancellationToken);
                        var replay = await CollectionAttemptImporter.ImportAsync(attempt.AttemptId,
                            entry.Request, entry.Receipt, attempts, cancellationToken);
                        if (Equals(replay.AttemptResult, retained.AttemptResult)) verifiedReceipt = entry.Receipt;
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Retained evidence still settles safely with the full reservation.
                }
            }

            using var settleTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (!lease.IsOwned) return false;
            var resolution = verifiedReceipt is null ? CollectionJobLifecycle.Resolve(retained) :
                CollectionJobLifecycle.Resolve(verifiedReceipt);
            var settledRetained = await lease.SettleAttemptAsync(attempt.AttemptId, resolution, settleTimeout.Token);
            if (!settledRetained) return false;
            if (verifiedReceipt is not null)
            {
                try { await handoffs.DeleteAsync(attempt.AttemptId, settleTimeout.Token); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException) { }
            }
            return true;
        }

        await using var recoveryLease = await handoffs.AcquireRecoveryLeaseAsync(cancellationToken);
        var handoffIds = await handoffs.ListAsync(cancellationToken);
        if (!handoffIds.Contains(attempt.AttemptId, StringComparer.Ordinal))
        {
            using var interruptionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await lease.SettleAttemptAsync(attempt.AttemptId,
                new CollectionAttemptResolution(CollectionJobAttemptOutcome.Interrupted, null, "ReceiptUnavailable"),
                interruptionTimeout.Token);
        }

        PendingCollectionHandoff handoff;
        try
        {
            handoff = await handoffs.LoadAsync(attempt.AttemptId, cancellationToken);
            if (handoff.Request != attempt.Request) return false;
            await verifier.VerifyAsync(handoff.Request, handoff.Receipt, Path.GetFullPath(artifactDirectory), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }

        if (!lease.IsOwned) return false;
        if (!await ImportReceiptAsync(attempt.AttemptId, handoff.Request, handoff.Receipt, cancellationToken))
            return false;
        using var settleHandoffTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (!lease.IsOwned) return false;
        var settled = await lease.SettleAttemptAsync(attempt.AttemptId,
            CollectionJobLifecycle.Resolve(handoff.Receipt), settleHandoffTimeout.Token);
        if (!settled) return false;
        try { await handoffs.DeleteAsync(attempt.AttemptId, settleHandoffTimeout.Token); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException) { }
        return true;
    }

    private async Task<bool> ImportReceiptAsync(string attemptId, CollectionRequest request,
        CollectionResult receipt, CancellationToken cancellationToken)
    {
        try
        {
            await CollectionAttemptImporter.ImportAsync(attemptId, request, receipt, attempts, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<CollectionJobRecord?> ReadJobBoundedAsync(string jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { return await jobs.GetAsync(jobId, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return null; }
    }

    private static bool IsTerminal(CollectionJobState state) =>
        state is CollectionJobState.Succeeded or CollectionJobState.Failed or CollectionJobState.Cancelled;

    private static CollectionJobRunStatus MapStatus(CollectionJobStartStatus status) => status switch
    {
        CollectionJobStartStatus.Blocked => CollectionJobRunStatus.Blocked,
        CollectionJobStartStatus.Exhausted => CollectionJobRunStatus.Exhausted,
        CollectionJobStartStatus.Cancelled => CollectionJobRunStatus.Cancelled,
        _ => CollectionJobRunStatus.LostOwnership
    };
}
