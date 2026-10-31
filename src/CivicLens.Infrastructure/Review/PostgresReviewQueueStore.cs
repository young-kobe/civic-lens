using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

/// <summary>Reads the records that wait for a person, newest revision first, across the seven draft tables.</summary>
public sealed class PostgresReviewQueueStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IReviewQueueStore
{
    private const string SearchTextParameter = "{0}";

    private static readonly QueueKind[] Kinds =
    [
        new(PublishedRecordKind.DocumentChange, "dc", "document_change_drafts", "headline",
            (db, ids, ct) => PostgresDocumentChangeReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Statement, "st", "statement_drafts", "headline",
            (db, ids, ct) => PostgresStatementReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Action, "ac", "action_drafts", "headline",
            (db, ids, ct) => PostgresActionReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Vote, "vo", "vote_drafts", "headline",
            (db, ids, ct) => VoteDraftRows.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Brief, "br", "brief_drafts", "headline",
            (db, ids, ct) => PostgresBriefReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Relationship, "re", "relationship_drafts", null,
            ReadRelationshipsAsync),
        new(PublishedRecordKind.ExecutiveAction, "ea", "executive_action_drafts", "title",
            (db, ids, ct) => ExecutiveActionRows.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Title, ct))
    ];

    private static readonly Dictionary<string, QueueKind> KindsByCode = Kinds.ToDictionary(kind => kind.Code);

    private static readonly string NeedsAPerson = string.Join("\nUNION ALL\n", Kinds.Select(kind => $"""
        SELECT '{kind.Code}' AS "KindCode", {kind.IdColumn} AS "RecordId", '{kind.Code}-' || {kind.IdColumn} AS "QueueId",
               last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks"
        FROM {kind.Table}
        WHERE review_status <> 'Approved' AND current_revision_number >= 1
        """));

    private const string UnmatchedApprovedLinks = """
        SELECT 're' AS "KindCode", record_id AS "RecordId", 're-' || record_id AS "QueueId",
               last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks"
        FROM relationship_drafts
        WHERE review_status = 'Approved' AND no_longer_matches AND NOT (record_id = ANY({0}))
        """;

    private static readonly string ApprovedHeadlineMatches = string.Join("\nUNION ALL\n", Kinds.Where(kind => kind.HeadlineProperty is not null)
        .Select(kind => $"""
        SELECT '{kind.Code}' AS "KindCode", draft.{kind.IdColumn} AS "RecordId", '{kind.Code}-' || draft.{kind.IdColumn} AS "QueueId",
               draft.last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks", revision.revision_json::jsonb ->> '{kind.HeadlineProperty}' AS "Headline"
        FROM {kind.Table} draft
        JOIN {ReviewTables.For(kind.Kind).Revisions} revision
            ON revision.{kind.IdColumn} = draft.{kind.IdColumn} AND revision.revision_number = draft.current_revision_number
        WHERE draft.review_status = 'Approved'
            AND to_tsvector('english', revision.revision_json::jsonb ->> '{kind.HeadlineProperty}') @@ websearch_to_tsquery('english', {SearchTextParameter})
        """));

    public async Task<ReviewQueuePage> ListAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var queue = db.Database.SqlQueryRaw<QueueKey>(NeedsAPerson);
        var fetched = await ReviewKeyset.Page(queue, cursor, key => key.LastRevisedAtUtcTicks, key => key.QueueId)
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<QueueKey>.Create(fetched, limit, cursor, key => (key.LastRevisedAtUtcTicks, key.QueueId));
        var drafts = await ReadDraftsAsync(db, slice.Items, cancellationToken);
        var total = await queue.CountAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. slice.Items.Select(key => ToItem(key, drafts[key.QueueId]))], total, slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<ReviewQueuePage> ListUnmatchedApprovedLinksAsync(int limit, IReadOnlyCollection<string> excludedRecordIds,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var links = db.Database.SqlQueryRaw<QueueKey>(UnmatchedApprovedLinks, (object)excludedRecordIds.ToArray());
        var keys = await links.OrderByDescending(key => key.LastRevisedAtUtcTicks).ThenByDescending(key => key.QueueId)
            .Take(limit).ToArrayAsync(cancellationToken);
        var drafts = await ReadDraftsAsync(db, keys, cancellationToken);
        var total = await links.CountAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. keys.Select(key => ToItem(key, drafts[key.QueueId]))], total, null, null);
    }

    public async Task<ApprovedRecordPage> SearchApprovedAsync(string text, PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var matches = db.Database.SqlQueryRaw<ApprovedKey>(ApprovedHeadlineMatches, text);
        var fetched = await ReviewKeyset.Page(matches, cursor, key => key.LastRevisedAtUtcTicks, key => key.QueueId)
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<ApprovedKey>.Create(fetched, limit, cursor, key => (key.LastRevisedAtUtcTicks, key.QueueId));
        return new([.. slice.Items.Select(key => new ApprovedRecordHit(KindsByCode[key.KindCode].Kind, key.RecordId, key.Headline))],
            slice.NewerCursor, slice.OlderCursor);
    }

    private static async Task<Dictionary<string, QueueDraft>> ReadDraftsAsync(CollectionAttemptDbContext db, IEnumerable<QueueKey> keys,
        CancellationToken cancellationToken)
    {
        Dictionary<string, QueueDraft> drafts = [];
        foreach (var group in keys.GroupBy(key => key.KindCode))
        {
            var kind = KindsByCode[group.Key];
            foreach (var (recordId, draft) in await kind.Read(db, [.. group.Select(key => key.RecordId)], cancellationToken))
                drafts[$"{kind.Code}-{recordId}"] = draft;
        }
        return drafts;
    }

    private static async Task<Dictionary<string, QueueDraft>> ReadRelationshipsAsync(CollectionAttemptDbContext db, string[] recordIds,
        CancellationToken cancellationToken)
    {
        var (reads, unreadable) = await RelationshipDraftRows.Records.ReadQueueAsync(db, recordIds, cancellationToken);
        var (headlines, unreadableStatements) = await RelationshipCitedReader.ReadStatementHeadlinesAsync(db,
            [.. reads.Values.Select(read => read.Revision.Statement)], cancellationToken);
        Dictionary<string, QueueDraft> drafts = [];
        foreach (var (recordId, read) in reads)
        {
            var citation = read.Revision.Statement;
            drafts[recordId] = headlines.TryGetValue(citation, out var headline) ? read.ToDraft(headline, read.Revision.Type)
                : QueueDraft.Unreadable(recordId, unreadableStatements[citation]);
        }
        foreach (var (recordId, cause) in unreadable) drafts[recordId] = QueueDraft.Unreadable(recordId, cause);
        return drafts;
    }

    private static ReviewQueueItem ToItem(QueueKey key, QueueDraft draft)
    {
        var kind = KindsByCode[key.KindCode].Kind;
        var revisedAt = new DateTimeOffset(key.LastRevisedAtUtcTicks, TimeSpan.Zero);
        return draft.UnreadableCause is { } cause
            ? ReviewQueueItem.Unreadable(kind, key.RecordId, revisedAt, cause)
            : new(kind, key.RecordId, draft.Headline, draft.Status, draft.OpenConcerns, draft.HeldReason, revisedAt, draft.EditedByPerson,
                draft.LinkType);
    }

    private sealed record QueueKind(PublishedRecordKind Kind, string Code, string Table,
        string? HeadlineProperty,
        Func<CollectionAttemptDbContext, string[], CancellationToken, Task<Dictionary<string, QueueDraft>>> Read)
    {
        public string IdColumn => ReviewTables.For(Kind).IdColumn;
    }

    private class QueueKey
    {
        public string KindCode { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string QueueId { get; set; } = "";
        public long LastRevisedAtUtcTicks { get; set; }
    }

    private sealed class ApprovedKey : QueueKey
    {
        public string Headline { get; set; } = "";
    }
}
