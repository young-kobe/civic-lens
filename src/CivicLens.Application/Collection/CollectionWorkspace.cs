using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Application.Collection;

/// <summary>Owner-authorized collection and evidence preparation using server-configured sources and storage.</summary>
public sealed class CollectionWorkspace
{
    public const int RecentJobLimit = 50;
    private readonly CollectionConfigurationRevision revision;
    private readonly string artifactRoot;
    private readonly ICollectionJobStore jobs;
    private readonly IDiscoveryAdmissionStore discovery;
    private readonly ExtractDocument extract;
    private readonly IDocumentExtractionStore extractions;
    private readonly CompareDocuments compare;
    private readonly GetDocumentHistory history;
    private readonly ICollectionAttemptStore attempts;

    public CollectionWorkspace(CollectionConfiguration configuration, string artifactRoot, ICollectionJobStore jobs,
        IDiscoveryAdmissionStore discovery, ExtractDocument extract, IDocumentExtractionStore extractions,
        CompareDocuments compare, GetDocumentHistory history, ICollectionAttemptStore attempts)
    {
        revision = CollectionConfigurationRevision.Create(configuration);
        if (!Path.IsPathFullyQualified(artifactRoot)) throw new ArgumentException("Artifact root must be absolute.", nameof(artifactRoot));
        this.artifactRoot = artifactRoot;
        this.jobs = jobs;
        this.discovery = discovery;
        this.extract = extract;
        this.extractions = extractions;
        this.compare = compare;
        this.history = history;
        this.attempts = attempts;
    }

    public CollectionConfiguration GetConfiguration(ReviewActor actor)
    {
        RequireOwner(actor);
        return revision.ReadConfiguration();
    }

    public async Task<CollectionWorkspaceSources> GetSourcesAsync(ReviewActor actor, string? jobId, CancellationToken cancellationToken)
    {
        RequireOwner(actor);
        if (jobId is not null) ValidateJobId(jobId);
        var recentJobs = await jobs.ListAsync(RecentJobLimit, cancellationToken);
        if (jobId is null)
            return new CollectionWorkspaceSources(recentJobs, null,
                new Dictionary<string, StoredCollectionAttempt>(StringComparer.Ordinal), null, false, false);

        var job = recentJobs.SingleOrDefault(item => item.JobId == jobId)
            ?? await jobs.GetAsync(jobId, cancellationToken)
            ?? throw new ArgumentException("Collection job was not found.");
        DocumentHistory? documentHistory = null;
        var historyLimitExceeded = false;
        var historyUnavailable = false;
        try
        {
            documentHistory = await history.ExecuteAsync(job.Definition.SourceId, job.Definition.Url,
                cancellationToken: cancellationToken);
        }
        catch (DocumentHistoryLimitException)
        {
            historyLimitExceeded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            historyUnavailable = true;
        }

        var receiptlessAttemptIds = job.Attempts.Where(item => item.Resolution?.Receipt is null)
            .Select(item => item.AttemptId).Distinct(StringComparer.Ordinal).ToArray();
        var retainedAttempts = new Dictionary<string, StoredCollectionAttempt>(StringComparer.Ordinal);
        if (job.Definition.Mode == CivicLens.Collection.Contracts.CollectionMode.Page && documentHistory is not null)
        {
            foreach (var observation in documentHistory.Observations)
            {
                var attemptId = observation.Attempt.AttemptResult.AttemptId;
                if (receiptlessAttemptIds.Contains(attemptId, StringComparer.Ordinal))
                    retainedAttempts.TryAdd(attemptId, observation.Attempt);
            }
        }
        foreach (var attemptId in receiptlessAttemptIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (retainedAttempts.ContainsKey(attemptId)) continue;
            var retained = await attempts.GetAsync(attemptId, cancellationToken);
            if (retained is null) continue;
            if (retained.AttemptResult.SourceId != job.Definition.SourceId ||
                retained.AttemptResult.RequestedUrl != job.Definition.Url)
                throw new InvalidDataException("Retained attempt does not match its collection job.");
            retainedAttempts.Add(attemptId, retained);
        }

        return new CollectionWorkspaceSources(recentJobs, job, retainedAttempts, documentHistory,
            historyLimitExceeded, historyUnavailable);
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

    public async Task<DiscoveryAdmissionResult> AdmitArticlesAsync(ReviewActor actor, string jobId, string attemptId,
        string requestKey, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(actor, jobId, cancellationToken);
        _ = FindAttempt(job, attemptId);
        return await discovery.AdmitAsync(new ConfiguredCollectionSource(revision.ReadConfiguration(), job.Definition.SourceId),
            attemptId, RequestKey(actor, "admit", requestKey), cancellationToken);
    }

    public async Task<DocumentExtraction> ExtractAsync(ReviewActor actor, string jobId, string attemptId, CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(actor, jobId, cancellationToken);
        _ = FindAttempt(job, attemptId);
        return await extract.ExecuteConfiguredAsync(revision.ReadConfiguration(), attemptId, artifactRoot, cancellationToken);
    }

    public async Task<DocumentComparison> CompareAsync(ReviewActor actor, string jobId, string beforeId, string afterId,
        CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(actor, jobId, cancellationToken);
        if (beforeId == afterId) throw new ArgumentException("Select two different saved versions.");
        var before = await ReadExtractionAsync(beforeId, job, cancellationToken);
        var after = await ReadExtractionAsync(afterId, job, cancellationToken);
        if (before.SourceAttempt.ObservedAt >= after.SourceAttempt.ObservedAt)
            throw new ArgumentException("Select an earlier observation before a later observation.");
        return await compare.ExecuteAsync(before, after, cancellationToken);
    }

    private async Task<DocumentExtraction> ReadExtractionAsync(string id, CollectionJobRecord job, CancellationToken cancellationToken)
    {
        if (id is null || id.Length != 64 || id.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new ArgumentException("Choose a saved extraction.");
        var result = await extractions.GetAsync(id, cancellationToken);
        if (result is null || result.SourceAttempt.SourceId != job.Definition.SourceId || result.SourceAttempt.RequestedUrl != job.Definition.Url)
            throw new ArgumentException("Both versions must belong to this source and exact URL.");
        return result;
    }

    private static CollectionJobAttempt FindAttempt(CollectionJobRecord job, string attemptId) =>
        job.Attempts.SingleOrDefault(attempt => attempt.AttemptId == attemptId)
        ?? throw new ArgumentException("Choose an attempt belonging to this job.");

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
        if (actor is null || actor.Role != ReviewRole.Owner || string.IsNullOrWhiteSpace(actor.Subject) || actor.Subject.Length > 256)
            throw new UnauthorizedAccessException("Collection operations require the owner.");
    }
}
