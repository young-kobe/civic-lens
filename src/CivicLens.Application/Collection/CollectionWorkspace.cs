using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection.Health;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection;

public sealed class CollectionWorkspace
{
    public const int RecentJobLimit = PageLimit.Default;
    private const int MaximumProgressBatch = 100;
    private readonly CollectionConfigurationRevision revision;
    private readonly ICollectionJobStore jobs;
    private readonly ICollectionAttemptStore attempts;
    private readonly IEvidenceProcessingStore? processing;

    public CollectionWorkspace(CollectionConfiguration configuration, ICollectionJobStore jobs,
        ICollectionAttemptStore attempts, IEvidenceProcessingStore? processing = null)
    {
        revision = CollectionConfigurationRevision.Create(configuration);
        this.jobs = jobs;
        this.attempts = attempts;
        this.processing = processing;
    }

    public CollectionConfiguration GetConfiguration(ReviewActor actor)
    {
        RequireOwner(actor);
        return revision.ReadConfiguration();
    }

    public async Task<SourceHealthReport> GetSourceHealthAsync(ReviewActor actor, CancellationToken cancellationToken)
    {
        RequireOwner(actor);
        var targets = SourceTargets();
        var latest = await ReadLatestJobsAsync(targets, [], cancellationToken);
        var progress = await ReadProgressAsync(latest, cancellationToken);
        return SourceHealthResolver.Report(targets, latest, progress);
    }

    public async Task<CollectionWorkspaceSources> GetSourcesAsync(ReviewActor actor, string? jobId, string? cursor,
        int limit = PageLimit.Default, CancellationToken cancellationToken = default)
    {
        RequireOwner(actor);
        if (jobId is not null) ValidateJobId(jobId);
        PageLimit.Validate(limit);
        var pageCursor = PageCursor.Parse(cursor);
        var page = await jobs.ListPageAsync(pageCursor, limit, cancellationToken);
        var recentJobs = page.Items;
        var targets = SourceTargets();
        var latest = await ReadLatestJobsAsync(targets, pageCursor is null ? recentJobs : [], cancellationToken);
        var selected = jobId is null ? null : await ReadSelectedJobAsync(recentJobs, jobId, cancellationToken);
        IReadOnlyList<CollectionJobRecord> listed = selected is null ? recentJobs : [.. recentJobs, selected];
        var progress = await ReadProgressAsync(listed.Concat(latest), cancellationToken);
        var retained = selected is null
            ? new Dictionary<string, StoredCollectionAttempt>(StringComparer.Ordinal)
            : await ReadReceiptlessAttemptsAsync(selected, cancellationToken);
        return new CollectionWorkspaceSources(recentJobs, selected, retained, SourceHealthResolver.ByJob(listed, progress),
            SourceHealthResolver.Report(targets, latest, progress), page.NewerCursor, page.OlderCursor);
    }

    private async Task<CollectionJobRecord> ReadSelectedJobAsync(IReadOnlyList<CollectionJobRecord> recentJobs, string jobId,
        CancellationToken cancellationToken) =>
        recentJobs.SingleOrDefault(item => item.JobId == jobId)
            ?? await jobs.GetAsync(jobId, cancellationToken)
            ?? throw new ArgumentException("Collection job was not found.");

    private async Task<Dictionary<string, StoredCollectionAttempt>> ReadReceiptlessAttemptsAsync(CollectionJobRecord job,
        CancellationToken cancellationToken)
    {
        var retained = new Dictionary<string, StoredCollectionAttempt>(StringComparer.Ordinal);
        var attemptIds = job.Attempts.Where(item => item.Resolution?.Receipt is null)
            .Select(item => item.AttemptId).Distinct(StringComparer.Ordinal);
        foreach (var attemptId in attemptIds)
        {
            var attempt = await attempts.GetAsync(attemptId, cancellationToken);
            if (attempt is null) continue;
            if (attempt.AttemptResult.SourceId != job.Definition.SourceId ||
                attempt.AttemptResult.RequestedUrl != job.Definition.Url)
                throw new InvalidDataException("Retained attempt does not match its collection job.");
            retained.Add(attemptId, attempt);
        }
        return retained;
    }

    private SourceCheckTarget[] SourceTargets() => revision.ReadConfiguration().Sources
        .Select(source => new SourceCheckTarget(source.Id, source.Url)).ToArray();

    private async Task<IReadOnlyList<CollectionJobRecord>> ReadLatestJobsAsync(SourceCheckTarget[] targets,
        IReadOnlyList<CollectionJobRecord> loaded, CancellationToken cancellationToken)
    {
        var latest = new List<CollectionJobRecord>();
        var missing = new List<SourceCheckTarget>();
        foreach (var target in targets)
        {
            var job = loaded.FirstOrDefault(candidate => SourceHealthResolver.Matches(candidate, target));
            if (job is null) missing.Add(target);
            else latest.Add(job);
        }
        if (missing.Count > 0) latest.AddRange(await jobs.ListLatestBySourceAsync(missing, cancellationToken));
        return latest;
    }

    private async Task<IReadOnlyList<EvidenceProcessingRecord>> ReadProgressAsync(IEnumerable<CollectionJobRecord> read,
        CancellationToken cancellationToken)
    {
        if (processing is null) return [];
        var progress = new List<EvidenceProcessingRecord>();
        foreach (var ids in read.Select(job => job.JobId).Distinct(StringComparer.Ordinal).Chunk(MaximumProgressBatch))
            progress.AddRange(await processing.GetByJobIdsAsync(ids, cancellationToken));
        return progress;
    }

    public async Task<CollectionJobRecord> GetJobAsync(ReviewActor actor, string jobId, CancellationToken cancellationToken)
    {
        RequireOwner(actor);
        ValidateJobId(jobId);
        return await jobs.GetAsync(jobId, cancellationToken) ?? throw new ArgumentException("Collection job was not found.");
    }

    public Task<CollectionJobRecord> EnqueueAsync(ReviewActor actor, string sourceId, string requestKey, CancellationToken cancellationToken)
    {
        RequireOwner(actor);
        var configuration = revision.ReadConfiguration();
        if (!configuration.Sources.Any(source => source.Id == sourceId)) throw new ArgumentException("Choose a configured source.");
        return jobs.EnqueueAsync(new ConfiguredCollectionSource(configuration, sourceId),
            RequestKey(actor, "collect", requestKey), cancellationToken);
    }

    public Task<CollectionJobRecord?> CancelAsync(ReviewActor actor, string jobId, CancellationToken cancellationToken)
    {
        RequireOwner(actor);
        ValidateJobId(jobId);
        return jobs.CancelAsync(jobId, cancellationToken);
    }

    public async Task<CollectionJobRecord> RecollectAsync(ReviewActor actor, string jobId, string requestKey, CancellationToken cancellationToken)
    {
        var prior = await GetJobAsync(actor, jobId, cancellationToken);
        if (prior.Definition.Mode != CivicLens.Collection.Contracts.CollectionMode.Page)
            throw new ArgumentException("Use the configured source to collect a discovery listing again.");
        var configuration = revision.ReadConfiguration();
        var source = configuration.Sources.SingleOrDefault(item => item.Id == prior.Definition.SourceId)
            ?? throw new ArgumentException("The source is no longer configured.");
        // A new observation uses current limits and coverage, but only the already retained article URL.
        var article = source with { Mode = CivicLens.Collection.Contracts.CollectionMode.Page, Url = prior.Definition.Url, ETag = null, LastModified = null };
        var selection = configuration with { Sources = configuration.Sources.Select(item => item.Id == source.Id ? article : item).ToArray() };
        return await jobs.EnqueueAsync(new ConfiguredCollectionSource(selection, source.Id), RequestKey(actor, "recollect", requestKey), cancellationToken);
    }

    private static string RequestKey(ReviewActor actor, string operation, string key)
    {
        ValidateJobId(key);
        var subjectHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Subject)));
        return $"workspace:{operation}:{subjectHash}:{key}";
    }

    private static void ValidateJobId(string value)
    {
        if (value is null || value.Length != 32 || value.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new ArgumentException("Expected a lowercase job or request identifier.");
    }

    private static void RequireOwner(ReviewActor actor)
    {
        if (actor is null || actor.Role != ReviewRole.Owner || string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256 ||
            ReviewAuthor.IsAnalysis(actor.Subject))
            throw new UnauthorizedAccessException("Collection operations require the owner.");
    }
}
