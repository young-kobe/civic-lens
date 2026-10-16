using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Briefs;
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
    public static Task<ImmutableArray<BriefRecord>> ReadAsync(CollectionAttemptDbContext db, ImmutableArray<PublishedRevisionBinding> bindings,
        CancellationToken cancellationToken) => ReadKindsAsync(db, bindings, approvedOnly: true, cancellationToken);

    public static Task<ImmutableArray<BriefRecord>> ReadExactAsync(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, CancellationToken cancellationToken) =>
        ReadKindsAsync(db, bindings, approvedOnly: false, cancellationToken);

    public static PublishedRevisionBinding BindingOf(BriefCitation citation) =>
        new(BriefCitationKinds.PublishedKindOf(citation.Kind), citation.RecordId, citation.RevisionNumber);

    public static async Task<HashSet<PublishedRevisionBinding>> FindChangedInActiveReleaseAsync(CollectionAttemptDbContext db,
        IEnumerable<BriefCitation> citations, CancellationToken cancellationToken) =>
        await FindChangedAsync(db, citations, await ReviewQueries.ReadActiveReleaseRecordsAsync(db, cancellationToken), cancellationToken);

    public static async Task<HashSet<PublishedRevisionBinding>> FindChangedAsync(CollectionAttemptDbContext db,
        IEnumerable<BriefCitation> citations, ImmutableArray<PublishedRevisionBinding> releaseRecords, CancellationToken cancellationToken)
    {
        var bindings = citations.Select(BindingOf).Distinct().ToArray();
        if (bindings.Length == 0) return [];
        var released = releaseRecords.ToDictionary(binding => (binding.Kind, binding.RecordId), binding => binding.RevisionNumber);
        var changed = bindings.Where(binding => released.GetValueOrDefault((binding.Kind, binding.RecordId)) != binding.RevisionNumber).ToHashSet();
        foreach (var kind in Enum.GetValues<BriefCitationKind>())
        {
            var publishedKind = BriefCitationKinds.PublishedKindOf(kind);
            changed.UnionWith(await ReadNotApprovedAsync(db, kind, [.. bindings.Where(binding => binding.Kind == publishedKind)],
                cancellationToken));
        }
        return changed;
    }

    private static async Task<ImmutableArray<BriefRecord>> ReadKindsAsync(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, bool approvedOnly, CancellationToken cancellationToken)
    {
        var records = new List<BriefRecord>();
        records.AddRange(await ReadKindAsync<DocumentChangeRevisionRow, DocumentChangeDraftRevision>(db, bindings,
            BriefCitationKind.DocumentChange, approvedOnly, BriefRecord.From, cancellationToken));
        records.AddRange(await ReadKindAsync<StatementRevisionRow, StatementDraftRevision>(db, bindings,
            BriefCitationKind.Statement, approvedOnly, BriefRecord.From, cancellationToken));
        records.AddRange(await ReadKindAsync<ActionRevisionRow, ActionDraftRevision>(db, bindings,
            BriefCitationKind.Action, approvedOnly, BriefRecord.From, cancellationToken));
        return [.. records.OrderBy(record => record.Kind).ThenBy(record => record.RecordId, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<BriefRecord>> ReadKindAsync<TRow, TRevision>(CollectionAttemptDbContext db,
        ImmutableArray<PublishedRevisionBinding> bindings, BriefCitationKind kind, bool approvedOnly,
        Func<TRevision, BriefRecord> toRecord, CancellationToken cancellationToken)
        where TRow : class, IReviewRevisionRow
        where TRevision : class, IReviewRevision
    {
        var publishedKind = BriefCitationKinds.PublishedKindOf(kind);
        var requested = bindings.Where(binding => binding.Kind == publishedKind).ToArray();
        var wanted = approvedOnly
            ? requested.Except(await ReadNotApprovedAsync(db, kind, requested, cancellationToken)).ToArray() : requested;
        if (wanted.Length == 0) return [];
        var (table, _, idColumn) = SourceOf(kind);
        var sql = $"SELECT r.* FROM {table} r JOIN unnest({{0}}, {{1}}) AS k(id, number) ON r.{idColumn} = k.id AND r.revision_number = k.number";
        var rows = await db.Set<TRow>().FromSqlRaw(sql, wanted.Select(binding => binding.RecordId).ToArray(),
            wanted.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking().ToArrayAsync(cancellationToken);
        if (rows.Length != wanted.Length)
            throw new InvalidDataException(approvedOnly
                ? $"A release names a {kind} revision that is not stored."
                : $"A brief cites a {kind} revision that is not stored.");
        return [.. rows.Select(row => toRecord(ReviewJson.Deserialize<TRevision>(row.RevisionJson, "revision")))];
    }

    private static async Task<PublishedRevisionBinding[]> ReadNotApprovedAsync(CollectionAttemptDbContext db, BriefCitationKind kind,
        PublishedRevisionBinding[] requested, CancellationToken cancellationToken)
    {
        if (requested.Length == 0) return [];
        var (_, decisionTable, idColumn) = SourceOf(kind);
        var sql = $$"""
            SELECT k.id AS "RecordId", k.number AS "RevisionNumber" FROM unnest({0}, {1}) AS k(id, number)
            WHERE (SELECT d.kind FROM {{decisionTable}} d WHERE d.{{idColumn}} = k.id AND d.revision_number = k.number
                   ORDER BY d.review_state_version DESC LIMIT 1) IS DISTINCT FROM 'Approve'
            """;
        var rows = await db.Database.SqlQueryRaw<NotApprovedRow>(sql, requested.Select(binding => binding.RecordId).ToArray(),
            requested.Select(binding => binding.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new PublishedRevisionBinding(BriefCitationKinds.PublishedKindOf(kind), row.RecordId, row.RevisionNumber))];
    }

    private static (string Table, string DecisionTable, string IdColumn) SourceOf(BriefCitationKind kind) => kind switch
    {
        BriefCitationKind.DocumentChange => ("document_change_revisions", "document_change_review_decisions", "draft_id"),
        BriefCitationKind.Statement => ("statement_revisions", "statement_review_decisions", "candidate_id"),
        BriefCitationKind.Action => ("action_revisions", "action_review_decisions", "record_id"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Citation kind has no record tables.")
    };

    private sealed class NotApprovedRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
