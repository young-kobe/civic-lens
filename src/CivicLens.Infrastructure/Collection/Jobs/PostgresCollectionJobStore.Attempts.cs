using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    private static JobAttemptRow NewAttemptRow(string attemptId, string jobId, int sequence, CollectionRequest request,
        long startedAt) => new()
        {
            AttemptId = attemptId,
            JobId = jobId,
            Sequence = sequence,
            RequestVersion = request.Version,
            RequestJobId = request.JobId,
            RequestSourceId = request.SourceId,
            RequestUrl = request.Url,
            RequestAllowedOrigin = request.AllowedOrigin,
            RequestAllowedPathPrefix = request.AllowedPathPrefix,
            RequestArtifactDirectory = request.ArtifactDirectory,
            RequestMode = request.Mode,
            RequestMaxCandidates = request.MaxCandidates,
            RequestMaxRequests = request.MaxRequests,
            RequestMaxBytes = request.MaxBytes,
            RequestTimeoutSeconds = request.TimeoutSeconds,
            RequestMinDelayMilliseconds = request.MinDelayMilliseconds,
            RequestETag = request.ETag,
            RequestLastModifiedUtcTicks = request.LastModified?.UtcDateTime.Ticks,
            RequestCredential = request.Credential,
            StartedAt = startedAt
        };

    private static CollectionRequest ToRequest(JobAttemptRow row) => new()
    {
        Version = row.RequestVersion,
        JobId = row.RequestJobId,
        SourceId = row.RequestSourceId,
        Url = row.RequestUrl,
        AllowedOrigin = row.RequestAllowedOrigin,
        AllowedPathPrefix = row.RequestAllowedPathPrefix,
        ArtifactDirectory = row.RequestArtifactDirectory,
        Mode = row.RequestMode,
        MaxCandidates = row.RequestMaxCandidates,
        MaxRequests = row.RequestMaxRequests,
        MaxBytes = row.RequestMaxBytes,
        TimeoutSeconds = row.RequestTimeoutSeconds,
        MinDelayMilliseconds = row.RequestMinDelayMilliseconds,
        ETag = row.RequestETag,
        LastModified = FromTicks(row.RequestLastModifiedUtcTicks),
        Credential = row.RequestCredential
    };

    private static CollectionAttemptResolution? ToResolution(JobAttemptRow row) => row.Outcome is { } outcome
        ? new CollectionAttemptResolution(outcome, row.ErrorCode,
            row.RetryDelayTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null, row.RobotsCrawlDelayMilliseconds)
        : null;

    private static void SetResolution(JobAttemptRow row, CollectionAttemptResolution resolution, long completedAt)
    {
        row.Outcome = resolution.Outcome;
        row.ErrorCode = resolution.ErrorCode;
        row.RetryDelayTicks = resolution.RetryDelay?.Ticks;
        row.RobotsCrawlDelayMilliseconds = resolution.RobotsCrawlDelayMilliseconds;
        row.CompletedAt = completedAt;
    }

    private static CollectionJobAttempt ToAttempt(JobAttemptRow row, IReadOnlyDictionary<string, AttemptTransfer> transfers) =>
        new(row.AttemptId, ToRequest(row), ToResolution(row), FromTicks(row.StartedAt)!.Value, FromTicks(row.CompletedAt),
            transfers.GetValueOrDefault(row.AttemptId));

    private static async Task<Dictionary<string, AttemptTransfer>> ReadTransfersAsync(CollectionAttemptDbContext db,
        IEnumerable<JobAttemptRow> attempts, CancellationToken cancellationToken)
    {
        var attemptIds = attempts.Select(attempt => attempt.AttemptId).ToArray();
        var rows = await db.Attempts.AsNoTracking()
            .Where(row => attemptIds.Contains(row.AttemptId) && row.RequestCount != null)
            .Select(row => new { row.AttemptId, row.RequestCount, row.BytesReceived }).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.AttemptId, row => new AttemptTransfer(row.RequestCount!.Value, row.BytesReceived!.Value),
            StringComparer.Ordinal);
    }
}
