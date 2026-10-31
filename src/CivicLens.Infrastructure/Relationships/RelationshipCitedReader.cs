using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Publication.Contracts;
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

    /// <summary>Reads the headline of each cited statement revision for the review queue. A revision that is not stored, or whose
    /// stored data cannot be parsed, gets its cause instead of failing the whole read. A failed query still fails the read.</summary>
    public static async Task<(Dictionary<RelationshipCitation, string> Headlines, Dictionary<RelationshipCitation, Exception> Unreadable)>
        ReadStatementHeadlinesAsync(CollectionAttemptDbContext db, IReadOnlyCollection<RelationshipCitation> citations,
            CancellationToken cancellationToken)
    {
        var named = citations.Where(citation => citation.Kind == RelationshipRecordKind.Statement).Distinct().ToArray();
        var stored = await CitedRevisions.ReadStoredRevisionsAsync(db, PublishedRecordKind.Statement,
            [.. named.Select(citation => new PublishedRevisionBinding(PublishedRecordKind.Statement, citation.RecordId, citation.RevisionNumber))],
            cancellationToken);
        Dictionary<RelationshipCitation, string> headlines = [];
        Dictionary<RelationshipCitation, Exception> unreadable = [];
        foreach (var citation in named)
        {
            try
            {
                var row = stored.FirstOrDefault(row => row.RecordId == citation.RecordId && row.RevisionNumber == citation.RevisionNumber)
                    ?? throw new InvalidDataException("A cited statement revision is not stored.");
                var revision = ReviewJson.Deserialize<StatementDraftRevision>(row.RevisionJson, "revision");
                if (revision.CandidateId != citation.RecordId || revision.RevisionNumber != citation.RevisionNumber || revision.Headline is null)
                    throw new InvalidDataException("The stored revision does not match its citation.");
                headlines[citation] = revision.Headline;
            }
            catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
            {
                unreadable[citation] = new InvalidDataException(
                    $"Cited statement {citation.RecordId} revision {citation.RevisionNumber} cannot be read.", exception);
            }
        }
        return (headlines, unreadable);
    }

    public static async Task<(ApprovedStatement Statement, RelationshipTarget Target)> ReadCitedAsync(CollectionAttemptDbContext db,
        RelationshipRevision revision, CancellationToken cancellationToken) =>
        (await ReadCitedAsync(db, [revision], cancellationToken))[revision.RecordId];

    public static async Task<Dictionary<string, (ApprovedStatement Statement, RelationshipTarget Target)>> ReadCitedAsync(
        CollectionAttemptDbContext db, IReadOnlyCollection<RelationshipRevision> revisions, CancellationToken cancellationToken)
    {
        var citations = revisions.SelectMany(revision => new[] { revision.Statement, revision.Target }).ToArray();
        var statements = (await ReadAsync<StatementDraftRevision>(db, RelationshipRecordKind.Statement, citations, cancellationToken))
            .ToDictionary(item => (item.CandidateId, item.RevisionNumber));
        var targets = (await ReadAsync<ActionDraftRevision>(db, RelationshipRecordKind.Action, citations, cancellationToken))
            .Select(RelationshipTarget.Of).Concat((await ReadAsync<VoteDraftRevision>(db, RelationshipRecordKind.Vote, citations, cancellationToken))
                .Select(RelationshipTarget.Of)).ToDictionary(item => item.Citation);
        var candidateIds = statements.Keys.Select(key => key.CandidateId).Distinct().ToArray();
        var articleTexts = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                                  where candidateIds.Contains(candidate.CandidateId)
                                  join extraction in db.Set<DocumentExtractionRow>().AsNoTracking() on candidate.ExtractionId equals extraction.ExtractionId
                                  select new { candidate.CandidateId, extraction.Text }).ToDictionaryAsync(item => item.CandidateId, item => item.Text, cancellationToken);
        return revisions.ToDictionary(revision => revision.RecordId, revision =>
        {
            var statement = statements[(revision.Statement.RecordId, revision.Statement.RevisionNumber)];
            return (new ApprovedStatement(statement, articleTexts[statement.CandidateId]), targets[revision.Target]);
        });
    }

    private static async Task<ImmutableArray<T>> ReadAsync<T>(CollectionAttemptDbContext db, RelationshipRecordKind kind,
        IEnumerable<RelationshipCitation> citations, CancellationToken cancellationToken)
    {
        var publishedKind = RelationshipRecordKinds.PublishedKindOf(kind);
        var named = citations.Where(citation => citation.Kind == kind)
            .Select(citation => new PublishedRevisionBinding(publishedKind, citation.RecordId, citation.RevisionNumber));
        var json = await CitedRevisions.ReadRevisionJsonAsync(db, publishedKind, named, cancellationToken);
        return [.. json.Select(item => ReviewJson.Deserialize<T>(item, "revision"))];
    }

    private static (string Table, string IdColumn) DraftsOf(RelationshipRecordKind kind) => kind switch
    {
        RelationshipRecordKind.Statement => ("statement_drafts", "candidate_id"),
        RelationshipRecordKind.Action => ("action_drafts", "record_id"),
        RelationshipRecordKind.Vote => ("vote_drafts", "record_id"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Relationship record kind has no draft table.")
    };

    private sealed class ApprovedRevisionRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
