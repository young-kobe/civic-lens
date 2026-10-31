using System.Data;
using CivicLens.Application.Paging;
using CivicLens.Application.Review;
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
    private static readonly QueueKind[] Kinds =
    [
        new(PublishedRecordKind.DocumentChange, "dc", "document_change_drafts",
            (db, ids, ct) => PostgresDocumentChangeReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Statement, "st", "statement_drafts",
            (db, ids, ct) => PostgresStatementReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Action, "ac", "action_drafts",
            (db, ids, ct) => PostgresActionReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Vote, "vo", "vote_drafts",
            (db, ids, ct) => VoteDraftRows.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Brief, "br", "brief_drafts",
            (db, ids, ct) => PostgresBriefReviewStore.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Headline, ct)),
        new(PublishedRecordKind.Relationship, "re", "relationship_drafts",
            ReadRelationshipsAsync),
        new(PublishedRecordKind.ExecutiveAction, "ea", "executive_action_drafts",
            (db, ids, ct) => ExecutiveActionRows.Records.ReadQueueDraftsAsync(db, ids, revision => revision.Title, ct))
    ];

    private static readonly Dictionary<string, QueueKind> KindsByCode = Kinds.ToDictionary(kind => kind.Code);

    private static readonly string NeedsAPerson = string.Join("\nUNION ALL\n", Kinds.Select(kind => $"""
        SELECT '{kind.Code}' AS "KindCode", {kind.IdColumn} AS "RecordId", '{kind.Code}-' || {kind.IdColumn} AS "QueueId",
               last_revised_at_utc_ticks AS "LastRevisedAtUtcTicks"
        FROM {kind.Table}
        WHERE review_status <> 'Approved' AND current_revision_number >= 1
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
        var reads = await RelationshipDraftRows.Records.ReadQueueAsync(db, recordIds, cancellationToken);
        var headlines = await RelationshipCitedReader.ReadHeadlinesAsync(db, [.. reads.Values.Select(read => read.Revision.Statement)],
            cancellationToken);
        return reads.ToDictionary(read => read.Key,
            read => read.Value.ToDraft(headlines[read.Value.Revision.Statement], read.Value.Revision.Type), StringComparer.Ordinal);
    }

    private static ReviewQueueItem ToItem(QueueKey key, QueueDraft draft) =>
        new(KindsByCode[key.KindCode].Kind, key.RecordId, draft.Headline, draft.Status, draft.OpenConcerns, draft.HeldReason,
            new DateTimeOffset(key.LastRevisedAtUtcTicks, TimeSpan.Zero), draft.EditedByPerson, draft.LinkType);

    private sealed record QueueKind(PublishedRecordKind Kind, string Code, string Table,
        Func<CollectionAttemptDbContext, string[], CancellationToken, Task<Dictionary<string, QueueDraft>>> Read)
    {
        public string IdColumn => ReviewTables.For(Kind).IdColumn;
    }

    private sealed class QueueKey
    {
        public string KindCode { get; set; } = "";
        public string RecordId { get; set; } = "";
        public string QueueId { get; set; } = "";
        public long LastRevisedAtUtcTicks { get; set; }
    }
}
