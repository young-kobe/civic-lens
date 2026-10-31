using CivicLens.Application.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

public sealed class PostgresRecordKindStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRecordKindStore
{
    // A statement ID names a candidate, so a candidate with no draft is still found.
    private static readonly (PublishedRecordKind Kind, string Table, string IdColumn)[] RecordTables =
    [
        (PublishedRecordKind.DocumentChange, "document_change_drafts", "draft_id"),
        (PublishedRecordKind.Statement, "statement_candidates", "candidate_id"),
        (PublishedRecordKind.Action, "action_drafts", "record_id"),
        (PublishedRecordKind.Vote, "vote_drafts", "record_id"),
        (PublishedRecordKind.Brief, "brief_drafts", "record_id"),
        (PublishedRecordKind.Relationship, "relationship_drafts", "record_id"),
        (PublishedRecordKind.ExecutiveAction, "executive_action_drafts", "record_id")
    ];

    private static readonly string RecordKindQuery = string.Join("\nUNION ALL\n", RecordTables.Select(table =>
        $$"""SELECT '{{table.Kind}}' AS "Value" FROM {{table.Table}} WHERE {{table.IdColumn}} = {0}"""));

    public async Task<PublishedRecordKind?> FindKindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.SqlQueryRaw<string>(RecordKindQuery, recordId).SingleOrDefaultAsync(cancellationToken) is { } kind
            ? Enum.Parse<PublishedRecordKind>(kind) : null;
    }
}
