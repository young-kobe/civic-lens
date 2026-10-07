using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection.Discovery;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IFeedAdmissionStore
{
    public Task<FeedAdmissionResult> AdmitAsync(FeedAdmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var inputJson = Write(new AdmissionInput(request.AttemptId, request.ArticleTemplate, request.Policy));
        return TransactionAsync(async (db, now) =>
        {
            var batch = await db.Set<FeedAdmissionBatchRow>().SingleOrDefaultAsync(
                row => row.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (batch is not null)
            {
                if (batch.InputJson != inputJson)
                    throw new ArgumentException("The admission idempotency key belongs to different input.", nameof(request));
                return Read<FeedAdmissionResult>(batch.ResultJson);
            }

            var discoveryRow = await db.Set<FeedDiscoveryRow>().SingleOrDefaultAsync(
                row => row.AttemptId == request.AttemptId, cancellationToken)
                ?? throw new ArgumentException("The feed attempt has no persisted discovery result.", nameof(request));
            if (!string.Equals(discoveryRow.SourceId, request.ArticleTemplate.SourceId, StringComparison.Ordinal))
                throw new ArgumentException("Feed attempt and article template have different source IDs.", nameof(request));

            var feedRequest = Read<CollectionRequest>(discoveryRow.RequestJson);
            feedRequest.Validate();
            if (feedRequest.Mode != CollectionMode.Feed || feedRequest.SourceId != discoveryRow.SourceId)
                throw new InvalidDataException("Stored feed request identity is invalid.");
            var discovery = Read<FeedDiscoveryResult>(discoveryRow.DiscoveryJson);
            discovery.ValidateAgainst(feedRequest);
            if (discovery.Status != FeedDiscoveryStatus.Parsed)
                throw new ArgumentException("Only parsed feed attempts can admit candidates.", nameof(request));
            ValidateTemplate(request.ArticleTemplate, feedRequest);

            var jobs = new List<FeedAdmissionJob>();
            var deferred = 0;
            var duplicates = 0;
            var reservedRequests = 0;
            long reservedBytes = 0;
            var reservedSeconds = 0;
            foreach (var url in discovery.Urls)
            {
                var hash = HashCandidate(request.ArticleTemplate.SourceId, url);
                var existing = await db.Set<FeedCandidateJobRow>().SingleOrDefaultAsync(
                    row => row.CandidateHash == hash, cancellationToken);
                if (existing is not null)
                {
                    if (!string.Equals(existing.SourceId, request.ArticleTemplate.SourceId, StringComparison.Ordinal) ||
                        !string.Equals(existing.Url, url, StringComparison.Ordinal))
                        throw new InvalidDataException("Feed candidate URL hash collision detected.");
                    duplicates++;
                    continue;
                }

                var definition = request.ArticleTemplate with { Url = url };
                if (!request.Policy.TryReserve(definition, jobs.Count, reservedRequests, reservedBytes,
                    reservedSeconds, out var reservation))
                {
                    deferred++;
                    continue;
                }

                var jobId = Guid.NewGuid().ToString("N");
                var idempotencyKey = "feed-admission/" + Guid.NewGuid().ToString("N");
                db.Add(new JobRow
                {
                    JobId = jobId,
                    IdempotencyKey = idempotencyKey,
                    DefinitionJson = Write(definition),
                    State = CollectionJobState.Pending,
                    CreatedAt = Ticks(now)
                });
                db.Add(new FeedCandidateJobRow
                {
                    SourceId = definition.SourceId,
                    CandidateHash = hash,
                    Url = url,
                    JobId = jobId
                });
                jobs.Add(new FeedAdmissionJob(jobId, url));
                reservedRequests = checked(reservedRequests + reservation.Requests);
                reservedBytes = checked(reservedBytes + reservation.Bytes);
                reservedSeconds = checked(reservedSeconds + reservation.TimeoutSeconds);
            }

            var result = new FeedAdmissionResult(jobs, deferred, duplicates);
            db.Add(new FeedAdmissionBatchRow
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

    private static void ValidateTemplate(CollectionJobDefinition template, CollectionRequest feedRequest)
    {
        template.Validate();
        if (template.Mode != CollectionMode.Page || template.ETag is not null || template.LastModified is not null ||
            template.SourceId != feedRequest.SourceId || template.AllowedOrigin != feedRequest.AllowedOrigin ||
            template.AllowedPathPrefix != feedRequest.AllowedPathPrefix)
            throw new ArgumentException("Article template must be an unvalidated Page job in the feed source scope.");
    }

    private static string HashCandidate(string sourceId, string url)
    {
        var identity = $"{sourceId.Length}:{sourceId}{url}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private sealed record AdmissionInput(string AttemptId, CollectionJobDefinition ArticleTemplate, FeedAdmissionPolicy Policy);
}
