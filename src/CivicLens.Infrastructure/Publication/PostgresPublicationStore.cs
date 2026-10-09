using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Infrastructure.Publication;

public sealed partial class PostgresPublicationStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    : IPublicationStore
{
    private const string PublishOperation = "publish-release";
    private const int MaximumPageSize = 100;
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PostgresPublicationStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<PublicationReleaseSummary?> GetLatestAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .OrderByDescending(candidate => candidate.ReleaseNumber).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToSummary(row);
    }

    public async Task<PublicationReleaseSummary?> GetAsync(int releaseNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReleaseNumber == releaseNumber, cancellationToken);
        return row is null ? null : ToSummary(row);
    }

    public async Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaximumPageSize);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .OrderByDescending(candidate => candidate.ReleaseNumber).Take(limit).ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToList();
    }

    public async Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        ValidateActorAndIdempotency(actorSubject, idempotencyKey, payloadHash);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReadReplayAsync(db, actorSubject, idempotencyKey, payloadHash, cancellationToken);
    }

    public async Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        ValidateActorAndIdempotency(actorSubject, idempotencyKey, payloadHash);
        ValidateCommit(commit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresDocumentChangeReviewStore.BeginWriteAsync(db, cancellationToken);
        var replay = await ReadReplayAsync(db, actorSubject, idempotencyKey, payloadHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        await RequireNextReleaseNumberAsync(db, commit.ReleaseNumber, cancellationToken);
        await RequireUnchangedReviewStateAsync(db, commit.AddedRecords, cancellationToken);
        var summary = new PublicationReleaseSummary(commit.ReleaseNumber, commit.DirectoryName,
            commit.PublishedAtUtc, commit.Records.Length);
        db.Add(ToRow(actorSubject, commit));
        db.Add(new ReviewIdempotencyRow
        {
            ActorSubject = actorSubject,
            Operation = PublishOperation,
            IdempotencyKey = idempotencyKey,
            PayloadHash = payloadHash,
            ResultJson = JsonSerializer.Serialize(summary, JsonOptions)
        });
        await SaveAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return summary;
    }

    private static async Task SaveAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            throw new PublicationConflictException("Another release already took this number or directory.");
        }
    }

    private static async Task RequireNextReleaseNumberAsync(CollectionAttemptDbContext db, int releaseNumber,
        CancellationToken cancellationToken)
    {
        var latest = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .MaxAsync(candidate => (int?)candidate.ReleaseNumber, cancellationToken) ?? 0;
        if (latest != releaseNumber - 1)
            throw new PublicationConflictException("Another release was committed first. Rebuild from the latest release.");
    }

    private static async Task RequireUnchangedReviewStateAsync(CollectionAttemptDbContext db,
        ImmutableArray<ReviewStateExpectation> expectations, CancellationToken cancellationToken)
    {
        if (expectations.IsEmpty) return;
        var ids = expectations.Select(expectation => expectation.DraftId).ToArray();
        var drafts = await db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .Where(row => ids.Contains(row.DraftId)).ToDictionaryAsync(row => row.DraftId, cancellationToken);
        foreach (var expectation in expectations)
        {
            if (!drafts.TryGetValue(expectation.DraftId, out var draft) ||
                draft.CurrentRevisionNumber != expectation.RevisionNumber ||
                draft.ReviewStateVersion != expectation.ReviewStateVersion)
                throw new PublicationConflictException("Review state changed after the release was built. Rebuild it.");
        }
    }

    private static async Task<PublicationReleaseSummary?> ReadReplayAsync(CollectionAttemptDbContext db,
        string actorSubject, string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        var row = await db.Set<ReviewIdempotencyRow>().AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.ActorSubject == actorSubject && candidate.Operation == PublishOperation &&
            candidate.IdempotencyKey == idempotencyKey, cancellationToken);
        if (row is null) return null;
        if (row.PayloadHash != payloadHash)
            throw new ArgumentException("Idempotency key was already used with a different request payload.");
        return JsonSerializer.Deserialize<PublicationReleaseSummary>(row.ResultJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored publication result is invalid.");
    }

    private static PublicationReleaseRow ToRow(string actorSubject, PublicationCommit commit) => new()
    {
        ReleaseNumber = commit.ReleaseNumber,
        DirectoryName = commit.DirectoryName,
        ActorSubject = actorSubject,
        PublishedAtUtcTicks = commit.PublishedAtUtc.UtcTicks,
        RecordCount = commit.Records.Length,
        RecordsJson = JsonSerializer.Serialize(commit.Records, JsonOptions)
    };

    private static PublicationReleaseSummary ToSummary(PublicationReleaseRow row) => new(row.ReleaseNumber,
        row.DirectoryName, new DateTimeOffset(row.PublishedAtUtcTicks, TimeSpan.Zero), row.RecordCount);

    private static void ValidateCommit(PublicationCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentOutOfRangeException.ThrowIfLessThan(commit.ReleaseNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(commit.ReleaseNumber, 999_999);
        if (!DirectoryNamePattern().IsMatch(commit.DirectoryName) ||
            !commit.DirectoryName.StartsWith($"{commit.ReleaseNumber:D6}-", StringComparison.Ordinal))
            throw new ArgumentException("Release directory name does not match the release number.", nameof(commit));
        if (commit.Records.IsDefault || commit.AddedRecords.IsDefault)
            throw new ArgumentException("Release records are required.", nameof(commit));
        if (commit.PublishedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Publish time must be UTC.", nameof(commit));
    }

    private static void ValidateActorAndIdempotency(string actorSubject, string key, string hash)
    {
        if (string.IsNullOrWhiteSpace(actorSubject) || actorSubject.Length > 256)
            throw new ArgumentException("Actor subject is invalid.", nameof(actorSubject));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256)
            throw new ArgumentException("Idempotency key is invalid.", nameof(key));
        if (!HashPattern().IsMatch(hash ?? string.Empty))
            throw new ArgumentException("Payload hash must be a lowercase SHA-256 value.", nameof(hash));
    }

    [GeneratedRegex("^[0-9]{6}-[0-9a-f]{32}$")]
    private static partial Regex DirectoryNamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();
}
