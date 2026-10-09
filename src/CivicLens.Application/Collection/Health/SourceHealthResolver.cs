using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Health;

internal static class SourceHealthResolver
{
    public static SourceHealthReport Report(IReadOnlyList<SourceCheckTarget> targets,
        IReadOnlyList<CollectionJobRecord> latestJobs, IReadOnlyList<EvidenceProcessingRecord> processing)
    {
        var records = processing.ToLookup(record => record.JobId, StringComparer.Ordinal);
        var health = targets.Select(target => Resolve(target.SourceId,
            latestJobs.FirstOrDefault(job => Matches(job, target)), records)).ToArray();
        return new(health, health.Count(item => item.NeedsAttention));
    }

    public static IReadOnlyDictionary<string, SourceHealth> ByJob(IEnumerable<CollectionJobRecord> jobs,
        IReadOnlyList<EvidenceProcessingRecord> processing)
    {
        var records = processing.ToLookup(record => record.JobId, StringComparer.Ordinal);
        return jobs.DistinctBy(job => job.JobId).ToDictionary(job => job.JobId,
            job => Resolve(job.Definition.SourceId, job, records), StringComparer.Ordinal);
    }

    public static bool Matches(CollectionJobRecord job, SourceCheckTarget target) =>
        job.Definition.SourceId == target.SourceId && job.Definition.Url == target.Url;

    private static SourceHealth Resolve(string sourceId, CollectionJobRecord? job, ILookup<string, EvidenceProcessingRecord> records)
    {
        if (job is null) return new(sourceId, SourceCheckState.NeverChecked, null, null, null, false);
        var checkedAt = job.Attempts.Max(attempt => attempt.CompletedAt) ?? job.CreatedAt;
        return job.State switch
        {
            CollectionJobState.Pending or CollectionJobState.Running => New(SourceCheckState.Checking),
            CollectionJobState.WaitingToRetry => New(SourceCheckState.Failed, retryAt: job.RetryAt),
            CollectionJobState.Failed => New(SourceCheckState.Failed, needsAttention: true),
            CollectionJobState.Cancelled => New(SourceCheckState.Cancelled),
            _ => FromProcessing(job, SelectRecord(records[job.JobId].ToArray()))
        };

        SourceHealth New(SourceCheckState state, DateTimeOffset? retryAt = null, string? comparisonId = null,
            bool needsAttention = false) => new(sourceId, state, checkedAt, retryAt, comparisonId, needsAttention, job.JobId);

        SourceHealth FromProcessing(CollectionJobRecord succeeded, EvidenceProcessingRecord? record)
        {
            if (record is null) return New(SourceCheckState.Checking);
            return record.Status switch
            {
                EvidenceProcessingStatus.Failed or EvidenceProcessingStatus.Blocked =>
                    New(SourceCheckState.Failed, needsAttention: true),
                EvidenceProcessingStatus.RetryWaiting => New(SourceCheckState.Checking, retryAt: record.RetryAt),
                EvidenceProcessingStatus.Succeeded => FromOutcome(succeeded, record),
                _ => New(SourceCheckState.Checking)
            };
        }

        SourceHealth FromOutcome(CollectionJobRecord succeeded, EvidenceProcessingRecord record)
        {
            if (succeeded.Definition.Mode is CollectionMode.Feed or CollectionMode.Html)
                return New(SourceCheckState.UpToDate);
            return record.Outcome switch
            {
                EvidenceProcessingOutcome.Changed => New(SourceCheckState.ChangeFound, comparisonId: record.ComparisonId),
                EvidenceProcessingOutcome.Baseline => New(SourceCheckState.BaselineSaved),
                _ => New(SourceCheckState.UpToDate)
            };
        }
    }

    /// <summary>A changed page wins, then any unfinished page, then the first page; the job's own "prepare" record is the last resort.</summary>
    private static EvidenceProcessingRecord? SelectRecord(EvidenceProcessingRecord[] all)
    {
        var pages = all.Where(record => record.AttemptId != "prepare").ToArray();
        return pages.FirstOrDefault(record => record.Outcome == EvidenceProcessingOutcome.Changed)
            ?? pages.FirstOrDefault(IsUnfinished) ?? pages.FirstOrDefault() ?? all.FirstOrDefault();
    }

    private static bool IsUnfinished(EvidenceProcessingRecord record) => record.Status is EvidenceProcessingStatus.Pending or
        EvidenceProcessingStatus.Running or EvidenceProcessingStatus.RetryWaiting or EvidenceProcessingStatus.WaitingForPredecessor;
}
