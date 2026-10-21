using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Core.Briefs;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Infrastructure.Publication;

public sealed class PostgresPublicationStore : IPublicationStore
{
    private const string PublishOperation = "publish-release";
    private const int MaximumPageSize = 100;
    private const string UniqueViolation = PostgresErrorCodes.UniqueViolation;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue;

    public PostgresPublicationStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new BriefDraftQueueMapping())) { }

    private PostgresPublicationStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefDraftRun> briefQueue)
    {
        this.contextFactory = contextFactory;
        this.briefQueue = briefQueue;
    }

    public static PostgresPublicationStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<PublicationState> GetStateAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var releases = db.Set<PublicationReleaseRow>().AsNoTracking();
        var latest = await releases.MaxAsync(row => (int?)row.ReleaseNumber, cancellationToken) ?? 0;
        var active = await releases.SingleOrDefaultAsync(row => row.IsActive, cancellationToken);
        return new(latest, active is null ? null : ToSummary(active));
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

    public async Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ReviewKeysBeforeBriefs, cancellationToken);
        await BriefStaleness.TakeLocksAsync(briefQueue, db, cancellationToken);
        var row = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ReleaseNumber == releaseNumber, cancellationToken)
            ?? throw new ArgumentException($"Release {releaseNumber} has not been committed.", nameof(releaseNumber));
        await BriefStaleness.RequeueAfterActivationAsync(briefQueue, db, releaseNumber, cancellationToken);
        await ClearActiveAsync(db, cancellationToken);
        await db.Set<PublicationReleaseRow>().Where(candidate => candidate.ReleaseNumber == releaseNumber)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.IsActive, true), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(row);
    }

    public async Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serve);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ReviewKeysInOrder, cancellationToken);
        var active = await db.Set<PublicationReleaseRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.IsActive, cancellationToken);
        if (active is not null) await serve(active.DirectoryName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return active is null ? null : ToSummary(active);
    }

    public async Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReviewIdempotency.ReadReplayAsync<PublicationReleaseSummary>(db, actorSubject, PublishOperation, idempotencyKey,
            payloadHash, cancellationToken);
    }

    public async Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        ReviewIdempotency.ValidateRequest(actorSubject, idempotencyKey, payloadHash);
        if (ReviewAuthor.IsAnalysis(actorSubject))
            throw new UnauthorizedAccessException("A model author cannot publish.");
        ValidateCommit(commit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.ReviewKeysBeforeBriefs, cancellationToken);
        await BriefStaleness.TakeLocksAsync(briefQueue, db, cancellationToken);
        var replay = await ReviewIdempotency.ReadReplayAsync<PublicationReleaseSummary>(db, actorSubject, PublishOperation, idempotencyKey,
            payloadHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        await RequireUnchangedReleasesAsync(db, commit, cancellationToken);
        await RequireUnchangedReviewStateAsync(db, commit, cancellationToken);
        await RequireCurrentCitationsAsync(db, commit, cancellationToken);
        await BriefStaleness.RequeueAfterPublicationAsync(briefQueue, db, commit, cancellationToken);
        await ClearActiveAsync(db, cancellationToken);
        var publishedAt = await DatabaseClock.ReadAsync(db, cancellationToken);
        var summary = new PublicationReleaseSummary(commit.ReleaseNumber, commit.DirectoryName, publishedAt.Offset, commit.Records.Length);
        db.Add(ToRow(actorSubject, commit, publishedAt));
        ReviewIdempotency.Add(db, actorSubject, PublishOperation, idempotencyKey, payloadHash, ReviewJson.Serialize(summary));
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

    private static Task ClearActiveAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        db.Set<PublicationReleaseRow>().Where(row => row.IsActive)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsActive, false), cancellationToken);

    private static async Task RequireUnchangedReleasesAsync(CollectionAttemptDbContext db, PublicationCommit commit,
        CancellationToken cancellationToken)
    {
        var releases = db.Set<PublicationReleaseRow>().AsNoTracking();
        var latest = await releases.MaxAsync(candidate => (int?)candidate.ReleaseNumber, cancellationToken) ?? 0;
        if (latest != commit.ReleaseNumber - 1)
            throw new PublicationConflictException("Another release was committed first. Rebuild from the latest release.");
        var active = await releases.Where(candidate => candidate.IsActive)
            .Select(candidate => (int?)candidate.ReleaseNumber).SingleOrDefaultAsync(cancellationToken);
        if (active != commit.BaseReleaseNumber)
            throw new PublicationConflictException("The active release changed after the release was built. Rebuild it.");
    }

    private static async Task RequireUnchangedReviewStateAsync(CollectionAttemptDbContext db, PublicationCommit commit,
        CancellationToken cancellationToken)
    {
        var named = commit.AddedRecords.Select(expectation => (expectation.Kind, expectation.RecordId))
            .Concat(commit.WithdrawnRecords.Select(record => (record.Kind, record.RecordId))).ToArray();
        var current = await ReadCurrentReviewStatesAsync(db, named, cancellationToken);
        foreach (var expectation in commit.AddedRecords)
        {
            if (!current.TryGetValue((expectation.Kind, expectation.RecordId), out var state) ||
                state.RevisionNumber != expectation.RevisionNumber || state.ReviewStateVersion != expectation.ReviewStateVersion)
                throw new PublicationConflictException("Review state changed after the release was built. Rebuild it.");
        }
        foreach (var reference in commit.WithdrawnRecords)
        {
            if (!current.TryGetValue((reference.Kind, reference.RecordId), out var state))
                throw new ArgumentException($"{reference} has no review record, so it cannot be withdrawn.");
            if (!PublishedRecordRules.IsWithdrawable(state.ReviewStatus))
                throw new ArgumentException(
                    $"{reference} is {state.ReviewStatus} in review. Only a record that review blocked can be withdrawn.");
        }
    }

    private static async Task RequireCurrentCitationsAsync(CollectionAttemptDbContext db, PublicationCommit commit,
        CancellationToken cancellationToken)
    {
        var cited = (await ReadAddedBriefCitationsAsync(db, commit.AddedRecords, cancellationToken))
            .Concat(await ReadAddedRelationshipCitationsAsync(db, commit.AddedRecords, cancellationToken)).ToArray();
        if ((await CitedRevisions.FindNotCurrentAsync(db, cited, commit.Records, cancellationToken)).Count > 0)
            throw new PublicationConflictException("A cited record changed after the release was built. Rebuild it.");
    }

    private static async Task<IEnumerable<PublishedRevisionBinding>> ReadAddedBriefCitationsAsync(CollectionAttemptDbContext db,
        ImmutableArray<ReviewStateExpectation> expectations, CancellationToken cancellationToken)
    {
        var briefs = expectations.Where(expectation => expectation.Kind == PublishedRecordKind.Brief).ToArray();
        if (briefs.Length == 0) return [];
        var rows = await db.Set<BriefRevisionRow>().FromSqlRaw(
                "SELECT r.* FROM brief_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.record_id = k.id AND r.revision_number = k.number",
                briefs.Select(brief => brief.RecordId).ToArray(), briefs.Select(brief => brief.RevisionNumber).ToArray())
            .AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.Select(row => ReviewJson.Deserialize<BriefRevision>(row.RevisionJson, "revision"))
            .SelectMany(revision => revision.Citations).Select(BriefRecordReader.BindingOf);
    }

    private static async Task<IEnumerable<PublishedRevisionBinding>> ReadAddedRelationshipCitationsAsync(CollectionAttemptDbContext db,
        ImmutableArray<ReviewStateExpectation> expectations, CancellationToken cancellationToken)
    {
        var relationships = expectations.Where(expectation => expectation.Kind == PublishedRecordKind.Relationship).ToArray();
        if (relationships.Length == 0) return [];
        var rows = await db.Set<RelationshipRevisionRow>().FromSqlRaw(
                "SELECT r.* FROM relationship_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.record_id = k.id AND r.revision_number = k.number",
                relationships.Select(relationship => relationship.RecordId).ToArray(),
                relationships.Select(relationship => relationship.RevisionNumber).ToArray())
            .AsNoTracking().ToArrayAsync(cancellationToken);
        return rows.Select(row => ReviewJson.Deserialize<RelationshipRevision>(row.RevisionJson, "revision"))
            .SelectMany(revision => new[] { revision.Statement, revision.Target }).Select(RelationshipRecordKinds.BindingOf);
    }

    private static async Task<Dictionary<(PublishedRecordKind Kind, string RecordId), ReviewState>>
        ReadCurrentReviewStatesAsync(CollectionAttemptDbContext db, (PublishedRecordKind Kind, string RecordId)[] named,
            CancellationToken cancellationToken)
    {
        var current = new Dictionary<(PublishedRecordKind, string), ReviewState>();
        await AddDocumentChangeStatesAsync(db, IdsOf(named, PublishedRecordKind.DocumentChange), current, cancellationToken);
        await AddStatementStatesAsync(db, IdsOf(named, PublishedRecordKind.Statement), current, cancellationToken);
        await AddActionStatesAsync(db, IdsOf(named, PublishedRecordKind.Action), current, cancellationToken);
        await AddVoteStatesAsync(db, IdsOf(named, PublishedRecordKind.Vote), current, cancellationToken);
        await AddRelationshipStatesAsync(db, IdsOf(named, PublishedRecordKind.Relationship), current, cancellationToken);
        await AddBriefStatesAsync(db, IdsOf(named, PublishedRecordKind.Brief), current, cancellationToken);
        return current;
    }

    private static async Task AddDocumentChangeStatesAsync(CollectionAttemptDbContext db, string[] draftIds,
        Dictionary<(PublishedRecordKind, string), ReviewState> current, CancellationToken cancellationToken)
    {
        if (draftIds.Length == 0) return;
        var changes = await db.Set<DocumentChangeDraftRow>().AsNoTracking()
            .Where(row => draftIds.Contains(row.DraftId)).ToArrayAsync(cancellationToken);
        foreach (var row in changes)
            current[(PublishedRecordKind.DocumentChange, row.DraftId)] = new(row.CurrentRevisionNumber, row.ReviewStateVersion, row.ReviewStatus);
    }

    private static async Task AddStatementStatesAsync(CollectionAttemptDbContext db, string[] candidateIds,
        Dictionary<(PublishedRecordKind, string), ReviewState> current, CancellationToken cancellationToken)
    {
        if (candidateIds.Length == 0) return;
        var statements = await db.Set<StatementDraftRow>().AsNoTracking()
            .Where(row => candidateIds.Contains(row.CandidateId)).ToArrayAsync(cancellationToken);
        foreach (var row in statements)
            current[(PublishedRecordKind.Statement, row.CandidateId)] = new(row.CurrentRevisionNumber, row.ReviewStateVersion, row.ReviewStatus);
    }

    private static async Task AddActionStatesAsync(CollectionAttemptDbContext db, string[] recordIds,
        Dictionary<(PublishedRecordKind, string), ReviewState> current, CancellationToken cancellationToken)
    {
        if (recordIds.Length == 0) return;
        var actions = await db.Set<ActionDraftRow>().AsNoTracking()
            .Where(row => recordIds.Contains(row.RecordId)).ToArrayAsync(cancellationToken);
        foreach (var row in actions)
            current[(PublishedRecordKind.Action, row.RecordId)] = new(row.CurrentRevisionNumber, row.ReviewStateVersion, row.ReviewStatus);
    }

    private static async Task AddVoteStatesAsync(CollectionAttemptDbContext db, string[] recordIds,
        Dictionary<(PublishedRecordKind, string), ReviewState> current, CancellationToken cancellationToken)
    {
        if (recordIds.Length == 0) return;
        var votes = await db.Set<VoteDraftRow>().AsNoTracking()
            .Where(row => recordIds.Contains(row.RecordId)).ToArrayAsync(cancellationToken);
        foreach (var row in votes)
            current[(PublishedRecordKind.Vote, row.RecordId)] = new(row.CurrentRevisionNumber, row.ReviewStateVersion, row.ReviewStatus);
    }

    private static async Task AddRelationshipStatesAsync(CollectionAttemptDbContext db, string[] recordIds,
        Dictionary<(PublishedRecordKind, string), ReviewState> current, CancellationToken cancellationToken)
    {
        if (recordIds.Length == 0) return;
        var relationships = await db.Set<RelationshipDraftRow>().AsNoTracking()
            .Where(row => recordIds.Contains(row.RecordId)).ToArrayAsync(cancellationToken);
        foreach (var row in relationships)
            current[(PublishedRecordKind.Relationship, row.RecordId)] = new(row.CurrentRevisionNumber, row.ReviewStateVersion, row.ReviewStatus);
    }

    private static async Task AddBriefStatesAsync(CollectionAttemptDbContext db, string[] recordIds,
        Dictionary<(PublishedRecordKind, string), ReviewState> current, CancellationToken cancellationToken)
    {
        if (recordIds.Length == 0) return;
        var briefs = await db.Set<BriefDraftRow>().AsNoTracking()
            .Where(row => recordIds.Contains(row.RecordId)).ToArrayAsync(cancellationToken);
        foreach (var row in briefs)
            current[(PublishedRecordKind.Brief, row.RecordId)] = new(row.CurrentRevisionNumber, row.ReviewStateVersion, row.ReviewStatus);
    }

    private static string[] IdsOf((PublishedRecordKind Kind, string RecordId)[] named, PublishedRecordKind kind) =>
        [.. named.Where(record => record.Kind == kind).Select(record => record.RecordId)];

    private readonly record struct ReviewState(int RevisionNumber, int ReviewStateVersion, ReviewStatus ReviewStatus);

    private static PublicationReleaseRow ToRow(string actorSubject, PublicationCommit commit, DatabaseTime publishedAt) => new()
    {
        ReleaseNumber = commit.ReleaseNumber,
        DirectoryName = commit.DirectoryName,
        ActorSubject = actorSubject,
        PublishedAtUtcTicks = publishedAt.Ticks,
        RecordCount = commit.Records.Length,
        RecordsJson = JsonSerializer.Serialize(commit.Records, JsonOptions),
        IsActive = true
    };

    private static PublicationReleaseSummary ToSummary(PublicationReleaseRow row) => new(row.ReleaseNumber,
        row.DirectoryName, new DateTimeOffset(row.PublishedAtUtcTicks, TimeSpan.Zero), row.RecordCount);

    private static void ValidateCommit(PublicationCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentOutOfRangeException.ThrowIfLessThan(commit.ReleaseNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(commit.ReleaseNumber, 999_999);
        if (commit.BaseReleaseNumber < 1 || commit.BaseReleaseNumber >= commit.ReleaseNumber)
            throw new ArgumentException("The base release must precede the new release.", nameof(commit));
        ReleasePaths.RequireDirectoryName(commit.DirectoryName);
        if (!commit.DirectoryName.StartsWith($"{commit.ReleaseNumber:D6}-", StringComparison.Ordinal))
            throw new ArgumentException("Release directory name does not match the release number.", nameof(commit));
        if (commit.Records.IsDefault || commit.AddedRecords.IsDefault || commit.WithdrawnRecords.IsDefault)
            throw new ArgumentException("Release records are required.", nameof(commit));
    }
}
