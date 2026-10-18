using System.Collections.Immutable;
using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipCitedReader
{
    public static async Task<HashSet<RelationshipCitation>> FindChangedAsync(CollectionAttemptDbContext db,
        IEnumerable<RelationshipCitation> citations, CancellationToken cancellationToken)
    {
        var changed = citations.ToHashSet();
        foreach (var group in changed.GroupBy(citation => citation.Kind).ToArray())
        {
            var (table, idColumn) = DraftsOf(group.Key);
            var sql = $$"""
                SELECT d.{{idColumn}} AS "RecordId", d.current_revision_number AS "RevisionNumber" FROM {{table}} d
                WHERE d.review_status = 'Approved' AND d.{{idColumn}} = ANY({0})
                """;
            var approved = await db.Database.SqlQueryRaw<ApprovedRevisionRow>(sql, (object)group.Select(citation => citation.RecordId).ToArray())
                .ToArrayAsync(cancellationToken);
            changed.ExceptWith(approved.Select(row => new RelationshipCitation(group.Key, row.RecordId, row.RevisionNumber)));
        }
        return changed;
    }

    public static async Task<Dictionary<RelationshipCitation, string>> ReadHeadlinesAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<RelationshipCitation> citations, CancellationToken cancellationToken)
    {
        var statements = await ReadAsync<StatementDraftRevision>(db, RelationshipRecordKind.Statement, citations, cancellationToken);
        var actions = await ReadAsync<ActionDraftRevision>(db, RelationshipRecordKind.Action, citations, cancellationToken);
        var votes = await ReadAsync<VoteDraftRevision>(db, RelationshipRecordKind.Vote, citations, cancellationToken);
        return statements.Select(item => (new RelationshipCitation(RelationshipRecordKind.Statement, item.CandidateId, item.RevisionNumber), item.Headline))
            .Concat(actions.Select(item => (new RelationshipCitation(RelationshipRecordKind.Action, item.RecordId, item.RevisionNumber), item.Headline)))
            .Concat(votes.Select(item => (new RelationshipCitation(RelationshipRecordKind.Vote, item.RecordId, item.RevisionNumber), item.Headline)))
            .ToDictionary(item => item.Item1, item => item.Headline);
    }

    public static async Task<(ApprovedStatement Statement, RelationshipTarget Target)> ReadCitedAsync(CollectionAttemptDbContext db,
        RelationshipRevision revision, CancellationToken cancellationToken)
    {
        var statement = (await ReadAsync<StatementDraftRevision>(db, RelationshipRecordKind.Statement, [revision.Statement], cancellationToken)).Single();
        var articleText = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                                 where candidate.CandidateId == statement.CandidateId
                                 join extraction in db.Set<DocumentExtractionRow>().AsNoTracking() on candidate.ExtractionId equals extraction.ExtractionId
                                 select extraction.Text).SingleAsync(cancellationToken);
        var target = revision.Target.Kind == RelationshipRecordKind.Action
            ? RelationshipTarget.Of((await ReadAsync<ActionDraftRevision>(db, RelationshipRecordKind.Action, [revision.Target], cancellationToken)).Single())
            : RelationshipTarget.Of((await ReadAsync<VoteDraftRevision>(db, RelationshipRecordKind.Vote, [revision.Target], cancellationToken)).Single());
        return (new ApprovedStatement(statement, articleText), target);
    }

    private static async Task<ImmutableArray<T>> ReadAsync<T>(CollectionAttemptDbContext db, RelationshipRecordKind kind,
        IEnumerable<RelationshipCitation> citations, CancellationToken cancellationToken)
    {
        var wanted = citations.Where(citation => citation.Kind == kind).Distinct().ToArray();
        if (wanted.Length == 0) return [];
        var (table, idColumn) = RevisionsOf(kind);
        var sql = $"SELECT r.revision_json AS \"Value\" FROM {table} r JOIN unnest({{0}}, {{1}}) AS k(id, number) ON r.{idColumn} = k.id AND r.revision_number = k.number";
        var json = await db.Database.SqlQueryRaw<string>(sql, wanted.Select(citation => citation.RecordId).ToArray(),
            wanted.Select(citation => citation.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        if (json.Length != wanted.Length) throw new InvalidDataException($"A relationship cites a {kind} revision that is not stored.");
        return [.. json.Select(item => ReviewJson.Deserialize<T>(item, "revision"))];
    }

    private static (string Table, string IdColumn) DraftsOf(RelationshipRecordKind kind) => kind switch
    {
        RelationshipRecordKind.Statement => ("statement_drafts", "candidate_id"),
        RelationshipRecordKind.Action => ("action_drafts", "record_id"),
        _ => ("vote_drafts", "record_id")
    };

    private static (string Table, string IdColumn) RevisionsOf(RelationshipRecordKind kind) => kind switch
    {
        RelationshipRecordKind.Statement => ("statement_revisions", "candidate_id"),
        RelationshipRecordKind.Action => ("action_revisions", "record_id"),
        _ => ("vote_revisions", "record_id")
    };

    private sealed class ApprovedRevisionRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
