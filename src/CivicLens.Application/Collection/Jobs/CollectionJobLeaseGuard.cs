namespace CivicLens.Application.Collection.Jobs;

/// <summary>Keeps one claimed job and its optional singleton collector lease alive for one run.</summary>
internal sealed class CollectionJobLeaseGuard : IAsyncDisposable
{
    private readonly ICollectionJobStore jobs;
    private readonly TimeSpan duration;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationTokenSource ownershipLost = new();
    private readonly CancellationTokenSource workCancellation;
    private readonly CancellationTokenSource collectorCancellation;
    private readonly CancellationTokenSource collectorToken;
    private readonly Task heartbeat;
    private CollectionJobLease jobLease;
    private CollectionCollectorLease? collectorLease;
    private int owned = 1;
    private int cancellationRequested;

    public CollectionJobLeaseGuard(ICollectionJobStore jobs, CollectionJobClaim claim, TimeSpan duration,
        CancellationToken callerToken)
    {
        this.jobs = jobs;
        this.duration = duration;
        jobLease = claim.Lease;
        workCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerToken, ownershipLost.Token);
        collectorCancellation = new CancellationTokenSource();
        collectorToken = CancellationTokenSource.CreateLinkedTokenSource(workCancellation.Token, collectorCancellation.Token);
        heartbeat = Task.Run(RenewLoopAsync, CancellationToken.None);
    }

    public CancellationToken WorkToken => workCancellation.Token;
    public CancellationToken CollectorToken => collectorToken.Token;
    public string JobId => jobLease.JobId;
    public bool IsOwned => Volatile.Read(ref owned) == 1;
    public bool CancellationRequested => Volatile.Read(ref cancellationRequested) == 1;

    public async Task<CollectionJobLease> CurrentJobLeaseAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return jobLease; }
        finally { gate.Release(); }
    }

    public async Task<bool> AttachCollectorAsync(CollectionCollectorLease lease, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsOwned) return false;
            collectorLease = lease;
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> ReleaseCollectorAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (collectorLease is null) return true;
            var releasedLease = collectorLease;
            collectorLease = null;
            if (!IsOwned) return false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RenewalTimeout());
            return await jobs.ReleaseCollectorAsync(jobLease, releasedLease, timeout.Token);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> SettleAttemptAsync(string attemptId, CollectionAttemptResolution resolution,
        CancellationToken cancellationToken)
    {
        if (!IsOwned) return false;
        var lease = await CurrentJobLeaseAsync(cancellationToken);
        if (!IsOwned) return false;
        return await jobs.SettleAttemptAsync(lease, attemptId, resolution, cancellationToken);
    }

    public async Task<bool> ReleaseClaimAsync(CancellationToken cancellationToken)
    {
        await StopHeartbeatAsync();
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsOwned) return false;
            var released = await jobs.ReleaseClaimAsync(jobLease, cancellationToken);
            if (released) Interlocked.Exchange(ref owned, 0);
            return released;
        }
        finally { gate.Release(); }
    }

    public async Task<CollectionJobStartResult> TryStartAttemptAsync(string artifactDirectory,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsOwned) return new CollectionJobStartResult(CollectionJobStartStatus.LostOwnership);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RenewalTimeout());
            return await jobs.TryStartAttemptAsync(jobLease, artifactDirectory, timeout.Token);
        }
        finally { gate.Release(); }
    }

    private async Task RenewLoopAsync()
    {
        var interval = TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(interval, stop.Token);
                await gate.WaitAsync(stop.Token);
                try
                {
                    using var renewalTimeout = new CancellationTokenSource(RenewalTimeout());
                    if (!await RenewUnderGateAsync(renewalTimeout.Token))
                    {
                        LoseOwnership();
                        return;
                    }
                }
                finally { gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch
        {
            LoseOwnership();
        }
    }

    private async Task<bool> RenewUnderGateAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RenewalTimeout());
        var renewal = await jobs.RenewAsync(jobLease, collectorLease, duration, timeout.Token);
        if (!renewal.Owned || renewal.JobLease is null || (collectorLease is not null && renewal.CollectorLease is null))
            return false;
        jobLease = renewal.JobLease;
        if (collectorLease is not null) collectorLease = renewal.CollectorLease;
        if (renewal.CancellationRequested)
        {
            Volatile.Write(ref cancellationRequested, 1);
            collectorCancellation.Cancel();
        }
        return true;
    }

    private TimeSpan RenewalTimeout() => TimeSpan.FromTicks(Math.Max(1, duration.Ticks / 3));

    private async Task StopHeartbeatAsync()
    {
        stop.Cancel();
        try { await heartbeat; }
        catch { LoseOwnership(); }
    }

    private void LoseOwnership()
    {
        if (Interlocked.Exchange(ref owned, 0) == 1)
            ownershipLost.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        await StopHeartbeatAsync();
        workCancellation.Dispose();
        collectorToken.Dispose();
        collectorCancellation.Dispose();
        ownershipLost.Dispose();
        stop.Dispose();
        gate.Dispose();
    }
}
