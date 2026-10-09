using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Paging;

namespace CivicLens.Tests.Fixtures;

/// <summary>Serves fixed read results and counts the reads the Application layer makes. Writes are unsupported.</summary>
internal sealed class FixedJobStore : ICollectionJobStore
{
    public IReadOnlyList<CollectionJobRecord> Page { get; init; } = [];
    public string? OlderCursor { get; init; }
    public IReadOnlyList<CollectionJobRecord> Latest { get; init; } = [];
    public CollectionJobActivity Activity { get; init; } = new([], []);
    public int PageReads { get; private set; }
    public List<IReadOnlyCollection<SourceCheckTarget>> LatestReads { get; } = [];
    public int ActivityReads { get; private set; }

    public Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        PageReads++;
        return Task.FromResult(new CollectionJobPage(Page, null, OlderCursor));
    }

    public Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(
        IReadOnlyCollection<SourceCheckTarget> targets, CancellationToken cancellationToken)
    {
        LatestReads.Add(targets);
        return Task.FromResult(Latest);
    }

    public Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken)
    {
        ActivityReads++;
        return Task.FromResult(Activity);
    }

    public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FixedProcessingStore : IEvidenceProcessingStore
{
    public IReadOnlyList<EvidenceProcessingRecord> Records { get; init; } = [];
    public IReadOnlyList<CollectionChangeEvent> Changes { get; init; } = [];
    public List<IReadOnlyList<string>> ReadJobIds { get; } = [];
    public int ChangeReads { get; private set; }

    public Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds, CancellationToken cancellationToken)
    {
        ReadJobIds.Add(jobIds);
        return Task.FromResult<IReadOnlyList<EvidenceProcessingRecord>>(Records.Where(record => jobIds.Contains(record.JobId)).ToArray());
    }

    public Task<IReadOnlyList<CollectionChangeEvent>> ListRecentChangesAsync(int limit, CancellationToken cancellationToken)
    {
        ChangeReads++;
        return Task.FromResult(Changes);
    }

    public Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<EvidenceProcessingRecord?> GetByAttemptIdAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> IsAwaitingPreparationAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId, string requestedUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken) => throw new NotSupportedException();
}
