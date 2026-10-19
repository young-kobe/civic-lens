using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection.Discovery;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IDiscoveryAdmissionStore
{
    internal const string DiscoveryAdmissionKeyPrefix = "discovery-admission/";

    public Task<DiscoveryAdmissionResult> AdmitAsync(ConfiguredCollectionSource source, string attemptId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return AdmitAsync(idempotencyKey,
            (existing, currentDate) => source.CreateDiscoveryAdmission(attemptId, idempotencyKey, currentDate, existing),
            cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitAutomaticCheckAsync(ConfiguredCollectionSource source, string attemptId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return AdmitAsync(idempotencyKey,
            (existing, currentDate) => source.CreateDiscoveryAdmission(attemptId, idempotencyKey, currentDate,
                existing, recheckKnownCandidates: true), cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitAsync(DiscoveryAdmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        if (request.RecheckKnownCandidates)
            throw new ArgumentException("Rechecking known candidates is reserved for automatic source checks.", nameof(request));
        return AdmitAsync(request.IdempotencyKey, (_, _) => request, cancellationToken);
    }

    private Task<DiscoveryAdmissionResult> AdmitAsync(string idempotencyKey,
        Func<CollectionJobDefinition?, DateOnly, DiscoveryAdmissionRequest> prepareRequest, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256)
            throw new ArgumentException("Admission idempotency key must contain 1 to 256 characters.", nameof(idempotencyKey));
        return TransactionAsync(async (db, now) =>
        {
            var batch = await db.Set<DiscoveryAdmissionBatchRow>().SingleOrDefaultAsync(
                row => row.IdempotencyKey == idempotencyKey, cancellationToken);
            var savedTemplate = batch is null ? null : await ReadAdmissionTemplateAsync(db, batch, cancellationToken);
            var request = prepareRequest(savedTemplate, DateOnly.FromDateTime(now.UtcDateTime));
            var storedTemplate = StoredJobDefinition.WithoutRevision(request.ArticleTemplate);
            var inputJson = request.RecheckKnownCandidates
                ? Write(new AutomaticAdmissionInput(request.AttemptId, storedTemplate, request.Policy,
                    request.ExpectedDiscoveryMode, request.RecheckKnownCandidates))
                : Write(new AdmissionInput(request.AttemptId, storedTemplate, request.Policy, request.ExpectedDiscoveryMode));
            if (batch is not null)
            {
                if (!MatchesAdmission(batch, inputJson, request))
                    throw new ArgumentException("The admission idempotency key belongs to different input.", nameof(request));
                return Read<DiscoveryAdmissionResult>(batch.ResultJson);
            }

            var discoveryRow = await db.Set<DiscoveryRow>().SingleOrDefaultAsync(
                row => row.AttemptId == request.AttemptId, cancellationToken)
                ?? throw new ArgumentException("The discovery attempt has no persisted discovery result.", nameof(request));
            if (!string.Equals(discoveryRow.SourceId, request.ArticleTemplate.SourceId, StringComparison.Ordinal))
                throw new ArgumentException("Discovery attempt and article template have different source IDs.", nameof(request));

            var discoveryRequest = Read<CollectionRequest>(discoveryRow.RequestJson);
            discoveryRequest.ValidateReadable();
            if (discoveryRequest.Mode != request.ExpectedDiscoveryMode)
                throw new ArgumentException("Configured discovery mode does not match the retained attempt.", nameof(request));
            if (discoveryRequest.SourceId != discoveryRow.SourceId)
                throw new InvalidDataException("Stored discovery request identity is invalid.");
            var discovery = Read<DiscoveryResult>(discoveryRow.DiscoveryJson);
            discovery.ValidateAgainst(discoveryRequest);
            if (discovery.Status != DiscoveryStatus.Parsed)
                throw new ArgumentException("Only parsed discovery attempts can admit candidates.", nameof(request));
            ValidateTemplate(request.ArticleTemplate, discoveryRequest);
            var jobWriter = new JobRowWriter(db, Ticks(now));
            var revisionId = await jobWriter.StoreAsync(request.ArticleTemplate.ConfigurationRevision, cancellationToken);

            var jobs = new List<DiscoveryAdmissionJob>();
            var deferred = 0;
            var duplicates = 0;
            var budget = request.Policy.OpenBudget(request.ArticleTemplate);
            var candidateHashes = new string[discovery.Urls.Length];
            var urlsByCandidateHash = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < discovery.Urls.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = discovery.Urls[index];
                var hash = HashCandidate(request.ArticleTemplate.SourceId, url);
                if (urlsByCandidateHash.TryGetValue(hash, out var existingUrl) &&
                    !string.Equals(existingUrl, url, StringComparison.Ordinal))
                    throw new InvalidDataException("Discovery candidate URL hash collision detected.");
                urlsByCandidateHash[hash] = url;
                candidateHashes[index] = hash;
            }
            var existingCandidates = candidateHashes.Length == 0
                ? []
                : await db.Set<DiscoveryCandidateJobRow>()
                    .Where(row => candidateHashes.Contains(row.CandidateHash))
                    .ToDictionaryAsync(row => row.CandidateHash, cancellationToken);
            var existingJobIds = existingCandidates.Values.Select(candidate => candidate.JobId).ToArray();
            var existingJobs = !request.RecheckKnownCandidates || existingJobIds.Length == 0
                ? new Dictionary<string, JobRow>(StringComparer.Ordinal) :
                await db.Set<JobRow>().Where(row => existingJobIds.Contains(row.JobId))
                    .ToDictionaryAsync(row => row.JobId, cancellationToken);
            var candidateIndex = 0;
            foreach (var url in discovery.Urls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = candidateHashes[candidateIndex++];
                var definition = request.ArticleTemplate with { Url = url };
                if (definition.IsListingPage())
                {
                    duplicates++;
                    continue;
                }
                if (existingCandidates.TryGetValue(hash, out var existing))
                {
                    if (!string.Equals(existing.SourceId, request.ArticleTemplate.SourceId, StringComparison.Ordinal) ||
                        !string.Equals(existing.Url, url, StringComparison.Ordinal))
                        throw new InvalidDataException("Discovery candidate URL hash collision detected.");
                    if (!request.RecheckKnownCandidates)
                    {
                        duplicates++;
                        continue;
                    }
                    var knownJob = existingJobs[existing.JobId];
                    if (knownJob.State is not (CollectionJobState.Succeeded or CollectionJobState.Failed or CollectionJobState.Cancelled))
                    {
                        duplicates++;
                        continue;
                    }
                }

                if (!budget.TryReserve())
                {
                    deferred++;
                    continue;
                }

                var job = await jobWriter.AddJobAsync(DiscoveryAdmissionKeyPrefix + Guid.NewGuid().ToString("N"),
                    definition, cancellationToken);
                if (existingCandidates.TryGetValue(hash, out var candidateRow))
                    candidateRow.JobId = job.JobId;
                else
                    db.Add(new DiscoveryCandidateJobRow
                    {
                        SourceId = definition.SourceId,
                        CandidateHash = hash,
                        Url = url,
                        JobId = job.JobId
                    });
                jobs.Add(new DiscoveryAdmissionJob(job.JobId, url));
            }

            var result = new DiscoveryAdmissionResult(jobs, deferred, duplicates);
            db.Add(new DiscoveryAdmissionBatchRow
            {
                AttemptId = request.AttemptId,
                IdempotencyKey = request.IdempotencyKey,
                InputJson = inputJson,
                ConfigurationRevisionId = revisionId,
                ResultJson = Write(result),
                CreatedAt = Ticks(now)
            });
            return result;
        }, cancellationToken);
    }

    private static async Task<CollectionJobDefinition> ReadAdmissionTemplateAsync(CollectionAttemptDbContext db,
        DiscoveryAdmissionBatchRow batch, CancellationToken cancellationToken)
    {
        using var input = JsonDocument.Parse(batch.InputJson);
        return await StoredJobDefinition.ReadAsync(db, input.RootElement.GetProperty("articleTemplate").GetRawText(),
            batch.ConfigurationRevisionId, cancellationToken);
    }

    private static bool MatchesAdmission(DiscoveryAdmissionBatchRow batch, string inputJson, DiscoveryAdmissionRequest request) =>
        batch.ConfigurationRevisionId == request.ArticleTemplate.ConfigurationRevision?.Id && SameJson(batch.InputJson, inputJson);

    private static bool SameJson(string stored, string current) =>
        JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(current));

    private static void ValidateTemplate(CollectionJobDefinition template, CollectionRequest discoveryRequest)
    {
        template.Validate();
        if (template.Mode != CollectionMode.Page || template.ETag is not null || template.LastModified is not null ||
            template.SourceId != discoveryRequest.SourceId || template.AllowedOrigin != discoveryRequest.AllowedOrigin ||
            template.AllowedPathPrefix != discoveryRequest.AllowedPathPrefix)
            throw new ArgumentException("Article template must be an unvalidated Page job in the discovery source scope.");
    }

    internal static string HashCandidate(string sourceId, string url)
    {
        var identity = $"{sourceId.Length}:{sourceId}{url}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private sealed record AdmissionInput(string AttemptId, CollectionJobDefinition ArticleTemplate, DiscoveryAdmissionPolicy Policy,
        CollectionMode ExpectedDiscoveryMode);
    private sealed record AutomaticAdmissionInput(string AttemptId, CollectionJobDefinition ArticleTemplate,
        DiscoveryAdmissionPolicy Policy, CollectionMode ExpectedDiscoveryMode, bool RecheckKnownCandidates);
}
