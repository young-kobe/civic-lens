using CivicLens.Application.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string ColumnsKey = "columns";
    private static readonly HashSet<string> NoAllowedAbsences = [];

    private readonly RevisionBackfill<StatementRevisionRow, StatementDraftRevision, StatementRevisionPage> backfill = new(contextFactory, new(
        "statement_revision_backfill", PostgresAdvisoryLock.StatementReview, row => row.Headline == null,
        StatementRevisionRows.ReadPageAsync, FillColumnsAsync, ReadMovable, DifferingFields, nameof(StatementRevisionRow.CandidateId)));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static Task FillColumnsAsync(CollectionAttemptDbContext db, StatementRevisionRow row, StatementDraftRevision revision,
        CancellationToken cancellationToken)
    {
        StatementRevisionRows.FillColumns(row, revision);
        return Task.CompletedTask;
    }

    private static (StatementDraftRevision? Revision, string? Refusal) ReadMovable(StatementRevisionRow row)
    {
        StatementDraftRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<StatementDraftRevision>(row.RevisionJson, "statement revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
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

    private static IReadOnlyList<string> DifferingFields(StatementRevisionRow row, StatementRevisionPage page)
    {
        StatementDraftRevision rebuilt;
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
