using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            var savedTemplate = batch is null ? null : ReadAdmissionTemplate(batch.InputJson);
            var request = prepareRequest(savedTemplate, DateOnly.FromDateTime(now.UtcDateTime));
            // Keep the old feed input shape so durable batch replays created before this generalization still match.
            var inputJson = request.RecheckKnownCandidates
                ? Write(new AutomaticAdmissionInput(request.AttemptId, request.ArticleTemplate, request.Policy,
                    request.ExpectedDiscoveryMode, request.RecheckKnownCandidates))
                : request.ExpectedDiscoveryMode == CollectionMode.Feed
                    ? Write(new LegacyAdmissionInput(request.AttemptId, request.ArticleTemplate, request.Policy))
                    : Write(new AdmissionInput(request.AttemptId, request.ArticleTemplate, request.Policy, request.ExpectedDiscoveryMode));
            if (batch is not null)
            {
                if (batch.InputJson != inputJson && !MatchesLegacyAdmission(batch.InputJson, request))
                    throw new ArgumentException("The admission idempotency key belongs to different input.", nameof(request));
                return Read<DiscoveryAdmissionResult>(batch.ResultJson);
            }

            var discoveryRow = await db.Set<DiscoveryRow>().SingleOrDefaultAsync(
                row => row.AttemptId == request.AttemptId, cancellationToken)
                ?? throw new ArgumentException("The discovery attempt has no persisted discovery result.", nameof(request));
            if (!string.Equals(discoveryRow.SourceId, request.ArticleTemplate.SourceId, StringComparison.Ordinal))
                throw new ArgumentException("Discovery attempt and article template have different source IDs.", nameof(request));

            var discoveryRequest = Read<CollectionRequest>(discoveryRow.RequestJson);
            discoveryRequest.Validate();
            if (discoveryRequest.Mode != request.ExpectedDiscoveryMode)
                throw new ArgumentException("Configured discovery mode does not match the retained attempt.", nameof(request));
            if (discoveryRequest.SourceId != discoveryRow.SourceId)
                throw new InvalidDataException("Stored discovery request identity is invalid.");
            var discovery = Read<DiscoveryResult>(discoveryRow.DiscoveryJson);
            discovery.ValidateAgainst(discoveryRequest);
            if (discovery.Status != DiscoveryStatus.Parsed)
                throw new ArgumentException("Only parsed discovery attempts can admit candidates.", nameof(request));
            ValidateTemplate(request.ArticleTemplate, discoveryRequest);

            var jobs = new List<DiscoveryAdmissionJob>();
            var deferred = 0;
            var duplicates = 0;
            var reservedRequests = 0;
            long reservedBytes = 0;
            var reservedSeconds = 0;
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
            if (request.RecheckKnownCandidates && candidateHashes.Length > 0)
                _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT pg_advisory_xact_lock(hashtext({request.ArticleTemplate.SourceId}), hashtext(candidate_hash))
                    FROM unnest({candidateHashes}) AS candidates(candidate_hash)
                    ORDER BY candidate_hash
                    """, cancellationToken);
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

                var definition = request.ArticleTemplate with { Url = url };
                if (!request.Policy.TryReserve(definition, jobs.Count, reservedRequests, reservedBytes,
                    reservedSeconds, out var reservation))
                {
                    deferred++;
                    continue;
                }

                var jobId = Guid.NewGuid().ToString("N");
                var idempotencyKey = "discovery-admission/" + Guid.NewGuid().ToString("N");
                db.Add(new JobRow
                {
                    JobId = jobId,
                    IdempotencyKey = idempotencyKey,
                    DefinitionJson = Write(definition),
                    State = CollectionJobState.Pending,
                    CreatedAt = Ticks(now)
                });
                if (existingCandidates.TryGetValue(hash, out var candidateRow))
                    candidateRow.JobId = jobId;
                else
                    db.Add(new DiscoveryCandidateJobRow
                    {
                        SourceId = definition.SourceId,
                        CandidateHash = hash,
                        Url = url,
                        JobId = jobId
                    });
                jobs.Add(new DiscoveryAdmissionJob(jobId, url));
                reservedRequests = checked(reservedRequests + reservation.Requests);
                reservedBytes = checked(reservedBytes + reservation.Bytes);
                reservedSeconds = checked(reservedSeconds + reservation.TimeoutSeconds);
            }

            var result = new DiscoveryAdmissionResult(jobs, deferred, duplicates);
            db.Add(new DiscoveryAdmissionBatchRow
            {
                AttemptId = request.AttemptId,
                IdempotencyKey = request.IdempotencyKey,
                InputJson = inputJson,
                ResultJson = Write(result),
                CreatedAt = Ticks(now)
            });
            return result;
        }, cancellationToken);
    }

    private static CollectionJobDefinition ReadAdmissionTemplate(string storedJson)
    {
        using var input = JsonDocument.Parse(storedJson);
        return ReadDefinition(input.RootElement.GetProperty("articleTemplate").GetRawText());
    }

    private static bool MatchesLegacyAdmission(string storedJson, DiscoveryAdmissionRequest request)
    {
        var existingTemplate = ReadAdmissionTemplate(storedJson);
        if (existingTemplate.ConfigurationRevision is not null || existingTemplate.CoverageAsOf is not null)
            return false;
        var template = request.ArticleTemplate with { ConfigurationRevision = null, CoverageAsOf = null };
        var legacyJson = request.ExpectedDiscoveryMode == CollectionMode.Feed
            ? Write(new LegacyAdmissionInput(request.AttemptId, template, request.Policy))
            : Write(new AdmissionInput(request.AttemptId, template, request.Policy, request.ExpectedDiscoveryMode));
        return storedJson == legacyJson;
    }

    private static void ValidateTemplate(CollectionJobDefinition template, CollectionRequest discoveryRequest)
    {
        template.Validate();
        if (template.Mode != CollectionMode.Page || template.ETag is not null || template.LastModified is not null ||
            template.SourceId != discoveryRequest.SourceId || template.AllowedOrigin != discoveryRequest.AllowedOrigin ||
            template.AllowedPathPrefix != discoveryRequest.AllowedPathPrefix)
            throw new ArgumentException("Article template must be an unvalidated Page job in the discovery source scope.");
    }

    private static string HashCandidate(string sourceId, string url)
    {
        var identity = $"{sourceId.Length}:{sourceId}{url}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private sealed record LegacyAdmissionInput(string AttemptId, CollectionJobDefinition ArticleTemplate, DiscoveryAdmissionPolicy Policy);
    private sealed record AdmissionInput(string AttemptId, CollectionJobDefinition ArticleTemplate, DiscoveryAdmissionPolicy Policy,
        CollectionMode ExpectedDiscoveryMode);
    private sealed record AutomaticAdmissionInput(string AttemptId, CollectionJobDefinition ArticleTemplate,
        DiscoveryAdmissionPolicy Policy, CollectionMode ExpectedDiscoveryMode, bool RecheckKnownCandidates);
}
