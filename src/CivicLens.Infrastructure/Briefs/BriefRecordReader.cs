using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefRecordReader
{
    public static async Task<ImmutableArray<BriefRecord>> ReadAsync(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, CancellationToken cancellationToken)
    {
        var records = new List<BriefRecord>();
        records.AddRange(await ReadKindAsync<DocumentChangeRevisionRow, DocumentChangeDraftRevision>(db, bindings,
            PublishedRecordKind.DocumentChange, "document_change_revisions", "document_change_review_decisions", "draft_id", BriefRecord.From, cancellationToken));
        records.AddRange(await ReadKindAsync<StatementRevisionRow, StatementDraftRevision>(db, bindings,
            PublishedRecordKind.Statement, "statement_revisions", "statement_review_decisions", "candidate_id", BriefRecord.From, cancellationToken));
        records.AddRange(await ReadKindAsync<ActionRevisionRow, ActionDraftRevision>(db, bindings,
            PublishedRecordKind.Action, "action_revisions", "action_review_decisions", "record_id", BriefRecord.From, cancellationToken));
        return [.. records.OrderBy(record => record.Kind).ThenBy(record => record.RecordId, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<BriefRecord>> ReadKindAsync<TRow, TRevision>(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, PublishedRecordKind kind, string table, string decisionTable, string idColumn,
        Func<TRevision, BriefRecord> toRecord, CancellationToken cancellationToken)
        where TRow : class, IReviewRevisionRow
        where TRevision : class, IReviewRevision
    {
        var requested = bindings.Where(binding => binding.Kind == kind).ToArray();
        if (requested.Length == 0) return [];
        var notApproved = await ReadNotApprovedAsync(db, requested, decisionTable, idColumn, cancellationToken);
        var wanted = requested.Where(binding => !notApproved.Contains(binding.RecordId)).ToArray();
        if (wanted.Length == 0) return [];
        var sql = $"SELECT r.* FROM {table} r JOIN unnest({{0}}, {{1}}) AS k(id, number) ON r.{idColumn} = k.id AND r.revision_number = k.number";
        var rows = await db.Set<TRow>().FromSqlRaw(sql, wanted.Select(binding => binding.RecordId).ToArray(),
            wanted.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking().ToArrayAsync(cancellationToken);
        if (rows.Length != wanted.Length)
            throw new InvalidDataException($"A release names a {kind} revision that is not stored.");
        return [.. rows.Select(row => toRecord(ReviewJson.Deserialize<TRevision>(row.RevisionJson, "revision")))];
    }

    private static async Task<HashSet<string>> ReadNotApprovedAsync(CollectionAttemptDbContext db, PublishedRevisionBinding[] bindings,
        string decisionTable, string idColumn, CancellationToken cancellationToken)
    {
        var sql = $$"""
            SELECT k.id AS "Value" FROM unnest({0}, {1}) AS k(id, number)
            WHERE (SELECT d.kind FROM {{decisionTable}} d WHERE d.{{idColumn}} = k.id AND d.revision_number = k.number
                   ORDER BY d.review_state_version DESC LIMIT 1) IS DISTINCT FROM 'Approve'
            """;
        var ids = await db.Database.SqlQueryRaw<string>(sql, bindings.Select(binding => binding.RecordId).ToArray(),
            bindings.Select(binding => binding.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        return [.. ids];
    }
}
