using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipRevisionRows
{
    public static void FillColumns(RelationshipRevisionRow row, RelationshipRevision revision)
    {
        row.StatementCandidateId = revision.Statement.RecordId;
        row.StatementRevisionNumber = revision.Statement.RevisionNumber;
        row.TargetKind = revision.Target.Kind;
        var isAction = revision.Target.Kind == RelationshipRecordKind.Action;
        row.TargetActionRecordId = isAction ? revision.Target.RecordId : null;
        row.TargetActionRevisionNumber = isAction ? revision.Target.RevisionNumber : null;
        row.TargetVoteRecordId = isAction ? null : revision.Target.RecordId;
        row.TargetVoteRevisionNumber = isAction ? null : revision.Target.RevisionNumber;
        row.MatchBasis = revision.Match.Basis;
        row.MatchExtractionId = revision.Match.Span.ExtractionId;
        row.MatchStart = revision.Match.Span.Start;
        row.MatchLength = revision.Match.Span.Length;
        row.Type = revision.Type;
        row.Reason = revision.Reason;
        row.StatementQuoteExtractionId = revision.StatementQuote.ExtractionId;
        row.StatementQuoteStart = revision.StatementQuote.Start;
        row.StatementQuoteLength = revision.StatementQuote.Length;
        row.TargetFactId = revision.TargetFactId;
        row.OfficialId = revision.OfficialId;
    }

    public static async Task<RelationshipRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<RelationshipRevisionRow> query,
        CancellationToken cancellationToken) => new(await query.ToListAsync(cancellationToken));

    public static async Task<IReadOnlyList<StoredRevision<RelationshipRevisionRow, RelationshipRevision>>> ReadAsync(CollectionAttemptDbContext db,
        IQueryable<RelationshipRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    /// <summary>The statement and the target that a revision cites. A row with null columns fails here, and the JSON is no fallback.</summary>
    public static (RelationshipCitation Statement, RelationshipCitation Target) CitationsOf(RelationshipRevisionRow row)
    {
        if (row.StatementCandidateId is null)
            throw NotBackfilled($"Relationship revision {row.RecordId} {row.RevisionNumber}");
        var target = row.TargetKind == RelationshipRecordKind.Action
            ? new RelationshipCitation(RelationshipRecordKind.Action, row.TargetActionRecordId!, row.TargetActionRevisionNumber!.Value)
            : new RelationshipCitation(RelationshipRecordKind.Vote, row.TargetVoteRecordId!, row.TargetVoteRevisionNumber!.Value);
        return (new(RelationshipRecordKind.Statement, row.StatementCandidateId, row.StatementRevisionNumber!.Value), target);
    }

    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the relationship revision backfill.");
}
