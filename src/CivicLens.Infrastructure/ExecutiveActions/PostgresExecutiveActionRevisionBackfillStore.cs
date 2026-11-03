using CivicLens.Application.Review;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.ExecutiveActions;

public sealed class PostgresExecutiveActionRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string ColumnsKey = "columns";
    private static readonly HashSet<string> NoAllowedAbsences = [];

    private readonly RevisionBackfill<ExecutiveActionRevisionRow, ExecutiveActionRevision, ExecutiveActionRevisionPage> backfill = new(contextFactory, new(
        "executive_action_revision_backfill", PostgresAdvisoryLock.ExecutiveActionReview, row => row.SourceId == null,
        ExecutiveActionRevisionRows.ReadPageAsync, FillColumnsAsync, ReadMovable, DifferingFields));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static Task FillColumnsAsync(CollectionAttemptDbContext db, ExecutiveActionRevisionRow row, ExecutiveActionRevision revision,
        CancellationToken cancellationToken)
    {
        ExecutiveActionRevisionRows.FillColumns(row, revision);
        return Task.CompletedTask;
    }

    private static (ExecutiveActionRevision? Revision, string? Refusal) ReadMovable(ExecutiveActionRevisionRow row)
    {
        ExecutiveActionRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<ExecutiveActionRevision>(row.RevisionJson, "executive action revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
        if (HasNullElements(revision)) return (null, RevisionBackfillReasons.InvalidShape);
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

    private static bool HasNullElements(ExecutiveActionRevision revision) =>
        !revision.Facts.IsDefault && revision.Facts.Any(fact => fact is null) || !revision.Notices.IsDefault && revision.Notices.Any(notice => notice is null);

    private static IReadOnlyList<string> DifferingFields(ExecutiveActionRevisionRow row, ExecutiveActionRevisionPage page)
    {
        ExecutiveActionRevision rebuilt;
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
