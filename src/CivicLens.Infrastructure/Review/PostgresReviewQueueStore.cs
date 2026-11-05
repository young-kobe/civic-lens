using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions;
using CivicLens.Infrastructure.Relationships;
using CivicLens.Infrastructure.Review.Persistence;
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
        new(PublishedRecordKind.DocumentChange, "dc", "document_change_drafts",
            (db, ids, ct) => PostgresDocumentChangeReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct),
            Column: new("headline", recordId => DocumentChangeRevisionRows.NotBackfilled($"The current revision of document change {recordId}"))),
        new(PublishedRecordKind.Statement, "st", "statement_drafts",
            (db, ids, ct) => PostgresStatementReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct),
            Column: new("headline", recordId => StatementRevisionRows.NotBackfilled($"The current revision of statement {recordId}"))),
        new(PublishedRecordKind.Action, "ac", "action_drafts",
            (db, ids, ct) => PostgresActionReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct),
            Column: new("headline", recordId => ActionRevisionRows.NotBackfilled($"The current revision of action {recordId}"))),
        new(PublishedRecordKind.Vote, "vo", "vote_drafts",
            (db, ids, ct) => VoteDraftRows.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct), Column: new("headline", recordId => VoteRevisionRows.NotBackfilled($"The current revision of vote {recordId}"))),
        new(PublishedRecordKind.Brief, "br", "brief_drafts",
            (db, ids, ct) => PostgresBriefReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct), Column: new("headline", recordId => BriefRevisionRows.NotBackfilled($"The current revision of brief {recordId}"))),
        new(PublishedRecordKind.Relationship, "re", "relationship_drafts",
            ReadRelationshipsAsync),
        new(PublishedRecordKind.ExecutiveAction, "ea", "executive_action_drafts",
            (db, ids, ct) => ExecutiveActionRows.ForReading.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Title, ct), Column: new("title", recordId => ExecutiveActionRevisionRows.NotBackfilled($"The current revision of executive action {recordId}")))
    ];

    private static readonly Dictionary<string, QueueKind> KindsByCode = Kinds.ToDictionary(kind => kind.Code);

    private static string KeysWhere(string statusFilter) => string.Join("\nUNION ALL\n", Kinds.Select(kind => $"""
        SELECT '{kind.Code}' AS "KindCode", {kind.IdColumn} AS "RecordId", '{kind.Code}-' || {kind.IdColumn} AS "QueueId",
               last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks"
        FROM {kind.Table}
        WHERE {statusFilter} AND current_revision_number >= 1
        """));

    private static readonly string NeedsAPerson = KeysWhere(ReviewDraftTableConfiguration.NeedsAPersonFilter);

    private static readonly string Rejected = KeysWhere("review_status = 'Rejected'");

    private const string UnmatchedApprovedLinks = """
        SELECT 're' AS "KindCode", record_id AS "RecordId", 're-' || record_id AS "QueueId",
               last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks"
        FROM relationship_drafts
        WHERE review_status = 'Approved' AND no_longer_matches AND NOT (record_id = ANY({0}))
        """;

    private static readonly string ApprovedHeadlineMatches = string.Join("\nUNION ALL\n", Kinds.Where(kind => kind.Column is not null)
        .Select(kind => $"""
        SELECT '{kind.Code}' AS "KindCode", draft.{kind.IdColumn} AS "RecordId", '{kind.Code}-' || draft.{kind.IdColumn} AS "QueueId",
               draft.last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks", {kind.HeadlineSql} AS "Headline"
        FROM {kind.Table} draft
        JOIN {ReviewTables.For(kind.Kind).Revisions} revision
            ON revision.{kind.IdColumn} = draft.{kind.IdColumn} AND revision.revision_number = draft.current_revision_number
        WHERE draft.review_status = 'Approved'
            AND ({kind.NullHeadlineMatch}to_tsvector('english', {kind.HeadlineSql}) @@ websearch_to_tsquery('english', {SearchTextParameter}))
        """));

    public Task<ReviewQueuePage> ListAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) =>
        ListKeysAsync(NeedsAPerson, cursor, limit, cancellationToken);

    public Task<ReviewQueuePage> ListRejectedAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) =>
        ListKeysAsync(Rejected, cursor, limit, cancellationToken);

    private async Task<ReviewQueuePage> ListKeysAsync(string keysSql, PageCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var queue = db.Database.SqlQueryRaw<QueueKey>(keysSql);
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
        return new([.. slice.Items.Select(key => new ApprovedRecordHit(KindsByCode[key.KindCode].Kind, key.RecordId,
            key.Headline ?? throw KindsByCode[key.KindCode].Column!.NotBackfilled(key.RecordId)))],
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
                draft.LinkType, RejectReason: draft.RejectReason);
    }

    private sealed record QueueKind(PublishedRecordKind Kind, string Code, string Table,
        Func<CollectionAttemptDbContext, string[], CancellationToken, Task<Dictionary<string, QueueDraft>>> Read, HeadlineColumn? Column = null)
    {
        public string IdColumn => ReviewTables.For(Kind).IdColumn;

        public string HeadlineSql => $"revision.{Column!.Name}";

        /// <summary>A row with a null headline matches, so that the read fails and the search does not skip the row.</summary>
        public string NullHeadlineMatch => $"{HeadlineSql} IS NULL OR ";
    }

    private sealed record HeadlineColumn(string Name, Func<string, InvalidDataException> NotBackfilled);

    private class QueueKey
    {
        public string KindCode { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string QueueId { get; set; } = "";
        public long LastRevisedAtUtcTicks { get; set; }
    }

    private sealed class ApprovedKey : QueueKey
    {
        public string? Headline { get; set; }
    }
}
