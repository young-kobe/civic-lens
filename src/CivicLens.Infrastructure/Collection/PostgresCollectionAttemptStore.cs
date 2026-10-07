using System.Data;
using System.Text.Json;
using CivicLens.Collection.Contracts;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Infrastructure.Collection.Discovery;
using CivicLens.Application.Collection;
using CivicLens.Core.Collection;
using CivicLens.Infrastructure.Collection.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CivicLens.Infrastructure.Collection;

/// <summary>Stores immutable collection observations in PostgreSQL.</summary>
public sealed class PostgresCollectionAttemptStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : ICollectionAttemptStore
{
    private const string ImportLockName = "civic-lens-collection-import";

    public async Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await GetAsync(db, attemptId, cancellationToken);
    }

    internal static async Task<StoredCollectionAttempt?> GetAsync(CollectionAttemptDbContext db, string attemptId,
        CancellationToken cancellationToken)
    {
        var row = await db.Attempts.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.AttemptId == attemptId, cancellationToken);
        return row is null ? null : await ReadStoredAttemptAsync(db, row, cancellationToken);
    }

    public static PostgresCollectionAttemptStore FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(parsed.Host) || string.IsNullOrWhiteSpace(parsed.Database))
                throw new ArgumentException("Host and Database are required.");
            var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>()
                .UseNpgsql(parsed.ConnectionString).Options;
            return new PostgresCollectionAttemptStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
        }
        catch (ArgumentException)
        {
            // Provider diagnostics can include connection-string values.
            throw new ArgumentException("CIVIC_LENS_DATABASE must be a valid Postgres connection string with Host and Database.");
        }
    }

    public async Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await AcquireImportLockAsync(db, cancellationToken);
            var existingRow = await db.Attempts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.AttemptId == attempt.AttemptResult.AttemptId, cancellationToken);
            var existing = existingRow is null ? null : await ReadStoredAttemptAsync(db, existingRow, cancellationToken);
            var candidates = existing is null
                ? await ReadPriorCapturesAsync(db, attempt.AttemptResult, cancellationToken)
                : [];
            var decision = attempt.DecideFromCandidates(existing, candidates);

            if (decision.Disposition == ImportDisposition.NewAttempt)
            {
                if (decision.AttemptResult is CapturedAttemptResult captured)
                {
                    await RetainCaptureAsync(db, captured.Capture, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                }
                db.Attempts.Add(ToRow(decision.AttemptResult, decision.PriorCapturedAttempt, decision.SentValidators));
                if (attempt.Discovery is { } discovery)
                    db.Add(new DiscoveryRow
                    {
                        AttemptId = decision.AttemptResult.AttemptId,
                        SourceId = decision.AttemptResult.SourceId,
                        RequestJson = JsonSerializer.Serialize(discovery.Request, CollectionProtocol.JsonOptions),
                        DiscoveryJson = JsonSerializer.Serialize(discovery.ToResult(), CollectionProtocol.JsonOptions)
                    });
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return decision;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Applies checked-in migrations explicitly; imports never run schema changes.</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task AcquireImportLockAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@lock_name, 0))";
        command.Parameters.AddWithValue("lock_name", ImportLockName);
        command.CommandTimeout = 0;
        await using (command)
            await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RetainCaptureAsync(CollectionAttemptDbContext db, CaptureIdentity capture,
        CancellationToken cancellationToken)
    {
        var retainedLength = await db.Captures.AsNoTracking().Where(row => row.Sha256 == capture.Sha256)
            .Select(row => (long?)row.ByteLength).SingleOrDefaultAsync(cancellationToken);
        if (retainedLength is long existingLength)
        {
            if (existingLength != capture.ByteLength)
                throw new InvalidOperationException("A capture hash was reused with a different byte length.");
            return;
        }

        db.Captures.Add(new CaptureRow { Sha256 = capture.Sha256, ByteLength = capture.ByteLength });
    }

    private static async Task<List<CapturedAttemptResult>> ReadPriorCapturesAsync(CollectionAttemptDbContext db,
        CollectionAttemptResult incoming, CancellationToken cancellationToken)
    {
        if (incoming is not NotModifiedAttemptResult || incoming.RequestedUrl != incoming.FinalUrl)
            return [];

        var rows = await db.Attempts.AsNoTracking()
            .Where(row => row.Outcome == "captured" && row.SourceId == incoming.SourceId &&
                row.RequestedUrl == incoming.RequestedUrl && row.FinalUrl == incoming.FinalUrl &&
                row.ObservedAtUtcTicks <= incoming.ObservedAt.UtcDateTime.Ticks)
            .OrderBy(row => row.ObservedAtUtcTicks).ThenBy(row => row.AttemptId)
            .ToListAsync(cancellationToken);
        var hashes = rows.Select(row => row.CaptureSha256!).Distinct(StringComparer.Ordinal).ToArray();
        var lengths = await db.Captures.AsNoTracking().Where(row => hashes.Contains(row.Sha256))
            .ToDictionaryAsync(row => row.Sha256, row => row.ByteLength, StringComparer.Ordinal, cancellationToken);
        return rows.Select(row => (CapturedAttemptResult)FromRow(row, lengths[row.CaptureSha256!])).ToList();
    }

    private static async Task<StoredCollectionAttempt> ReadStoredAttemptAsync(CollectionAttemptDbContext db,
        AttemptRow row, CancellationToken cancellationToken)
    {
        var captureLength = row.CaptureSha256 is null ? null : await db.Captures.AsNoTracking()
            .Where(capture => capture.Sha256 == row.CaptureSha256).Select(capture => (long?)capture.ByteLength)
            .SingleAsync(cancellationToken);
        var result = FromRow(row, captureLength);
        CapturedAttemptResult? prior = null;
        if (row.PriorCaptureAttemptId is not null)
        {
            var priorRow = await db.Attempts.AsNoTracking().SingleAsync(
                candidate => candidate.AttemptId == row.PriorCaptureAttemptId, cancellationToken);
            var priorLength = await db.Captures.AsNoTracking().Where(capture => capture.Sha256 == priorRow.CaptureSha256)
                .Select(capture => (long?)capture.ByteLength).SingleAsync(cancellationToken);
            prior = (CapturedAttemptResult)FromRow(priorRow, priorLength);
        }
        var discoveryRow = await db.Set<DiscoveryRow>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.AttemptId == row.AttemptId, cancellationToken);
        var discovery = discoveryRow is null ? null : new DiscoveryEvidence(
            JsonSerializer.Deserialize<CollectionRequest>(discoveryRow.RequestJson, CollectionProtocol.JsonOptions)!,
            JsonSerializer.Deserialize<DiscoveryResult>(discoveryRow.DiscoveryJson, CollectionProtocol.JsonOptions)!);
        return new StoredCollectionAttempt(result, row.HasSentValidators ? ReadSentValidators(row) : null, prior, discovery);
    }

    private static AttemptRow ToRow(CollectionAttemptResult result, CapturedAttemptResult? prior,
        SentValidators? validators)
    {
        var row = new AttemptRow
        {
            AttemptId = result.AttemptId,
            SourceId = result.SourceId,
            RequestedUrl = result.RequestedUrl,
            FinalUrl = result.FinalUrl,
            ObservedAtUtcTicks = result.ObservedAt.UtcDateTime.Ticks,
            HasSentValidators = validators is not null,
            SentETag = validators?.ETag,
            SentLastModifiedUtcTicks = validators?.LastModified?.UtcDateTime.Ticks,
            PriorCaptureAttemptId = prior?.AttemptId,
            PriorCaptureSha256 = prior?.Capture.Sha256
        };
        switch (result)
        {
            case CapturedAttemptResult captured:
                row.Outcome = "captured";
                row.CaptureSha256 = captured.Capture.Sha256;
                SetResponse(row, captured.Response);
                break;
            case NotModifiedAttemptResult notModified:
                row.Outcome = "notModified";
                SetResponse(row, notModified.Response);
                break;
            case FailedAttemptResult failed:
                row.Outcome = "failed";
                row.FailureCode = failed.FailureCode;
                if (failed.Response is not null) SetResponse(row, failed.Response);
                break;
            case DeferredAttemptResult deferred:
                row.Outcome = "deferred";
                row.FailureCode = deferred.FailureCode;
                row.RetryDelayTicks = deferred.RetryDelay?.Ticks;
                if (deferred.Response is not null) SetResponse(row, deferred.Response);
                break;
            default:
                throw new InvalidOperationException("Unknown collection attempt result type.");
        }
        return row;
    }

    private static void SetResponse(AttemptRow row, CollectionResponse response)
    {
        row.HasResponse = true;
        row.ResponseStatusCode = response.StatusCode;
        row.ResponseETag = response.ETag;
        row.ResponseLastModifiedUtcTicks = response.LastModified?.UtcDateTime.Ticks;
        row.ResponseContentType = response.ContentType;
        row.ResponseContentEncodings = response.ContentEncodings.ToArray();
    }

    private static CollectionAttemptResult FromRow(AttemptRow row, long? captureLength = null)
    {
        var observedAt = new DateTimeOffset(row.ObservedAtUtcTicks, TimeSpan.Zero);
        var response = row.HasResponse ? ReadResponse(row) : null;
        return row.Outcome switch
        {
            "captured" => new CapturedAttemptResult(row.AttemptId, row.SourceId, row.RequestedUrl, row.FinalUrl,
                observedAt, response!, new CaptureIdentity(row.CaptureSha256!, captureLength ??
                    throw new InvalidOperationException("Captured attempt has no capture length."))),
            "notModified" => new NotModifiedAttemptResult(row.AttemptId, row.SourceId, row.RequestedUrl, row.FinalUrl,
                observedAt, response!),
            "failed" => new FailedAttemptResult(row.AttemptId, row.SourceId, row.RequestedUrl, row.FinalUrl,
                observedAt, row.FailureCode!, response),
            "deferred" => new DeferredAttemptResult(row.AttemptId, row.SourceId, row.RequestedUrl, row.FinalUrl,
                observedAt, row.FailureCode!, row.RetryDelayTicks is long ticks ? TimeSpan.FromTicks(ticks) : null, response),
            _ => throw new InvalidOperationException($"Unknown persisted collection outcome '{row.Outcome}'.")
        };
    }

    private static CollectionResponse ReadResponse(AttemptRow row) => new(row.ResponseStatusCode!.Value,
        row.ResponseETag, FromUtcTicks(row.ResponseLastModifiedUtcTicks), row.ResponseContentType,
        row.ResponseContentEncodings!);

    private static SentValidators ReadSentValidators(AttemptRow row) => new(row.SentETag,
        FromUtcTicks(row.SentLastModifiedUtcTicks));

    private static DateTimeOffset? FromUtcTicks(long? ticks) => ticks is long value
        ? new DateTimeOffset(value, TimeSpan.Zero)
        : null;
}
