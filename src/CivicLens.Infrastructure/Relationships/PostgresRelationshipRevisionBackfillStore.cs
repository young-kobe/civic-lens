using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

public sealed class PostgresRelationshipRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string ColumnsKey = "columns";
    private static readonly HashSet<string> NoAllowedAbsences = [];

    private readonly RevisionBackfill<RelationshipRevisionRow, RelationshipRevision, RelationshipRevisionPage> backfill = new(contextFactory, new(
        "relationship_revision_backfill", PostgresAdvisoryLock.RelationshipReview, row => row.StatementCandidateId == null,
        RelationshipRevisionRows.ReadPageAsync, FillColumnsAsync, ReadMovable, DifferingFields));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static Task FillColumnsAsync(CollectionAttemptDbContext db, RelationshipRevisionRow row, RelationshipRevision revision,
        CancellationToken cancellationToken)
    {
        RelationshipRevisionRows.FillColumns(row, revision);
        return Task.CompletedTask;
    }

    private static (RelationshipRevision? Revision, string? Refusal) ReadMovable(RelationshipRevisionRow row)
    {
        RelationshipRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<RelationshipRevision>(row.RevisionJson, "relationship revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
        if (revision.Match is null) return (null, RevisionBackfillReasons.InvalidShape);
        try
        {
            revision.ValidateShape();
        }
        catch (ArgumentException)
        {
            return (null, RevisionBackfillReasons.InvalidShape);
        }
        return (revision, null);
    }

    private static IReadOnlyList<string> DifferingFields(RelationshipRevisionRow row, RelationshipRevisionPage page)
    {
        RelationshipRevision rebuilt;
        try
        {
            rebuilt = page.Rebuild(row);
        }
        catch (InvalidDataException)
        {
            return [ColumnsKey];
        }
        if (RevisionJsonComparison.TryParseObject(row.RevisionJson) is not { } stored) return [RevisionJsonComparison.UnreadableKey];
        return RevisionJsonComparison.DifferingKeys(stored, ReviewJson.ToObject(rebuilt), NoAllowedAbsences);
    }
}
