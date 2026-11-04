using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Actions;

public sealed class PostgresActionRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string ColumnsKey = "columns";
    private static readonly HashSet<string> NoAllowedAbsences = [];

    private readonly RevisionBackfill<ActionRevisionRow, ActionDraftRevision, ActionRevisionPage> backfill = new(contextFactory, new(
        "action_revision_backfill", PostgresAdvisoryLock.ActionReview, row => row.EventOn == null,
        ActionRevisionRows.ReadPageAsync, FillColumnsAsync, ReadMovable, DifferingFields));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static async Task FillColumnsAsync(CollectionAttemptDbContext db, ActionRevisionRow row, ActionDraftRevision revision,
        CancellationToken cancellationToken)
    {
        await DeleteChildRowsAsync(db, row, cancellationToken);
        ActionRevisionRows.FillColumns(row, revision);
    }

    private static async Task DeleteChildRowsAsync(CollectionAttemptDbContext db, ActionRevisionRow row, CancellationToken cancellationToken)
    {
        await db.Set<ActionRevisionSponsorshipCitationRow>().Where(citation => citation.RecordId == row.RecordId && citation.RevisionNumber == row.RevisionNumber)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Set<ActionRevisionSponsorshipRow>().Where(item => item.RecordId == row.RecordId && item.RevisionNumber == row.RevisionNumber)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Set<ActionRevisionIssueRow>().Where(issue => issue.RecordId == row.RecordId && issue.RevisionNumber == row.RevisionNumber)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static (ActionDraftRevision? Revision, string? Refusal) ReadMovable(ActionRevisionRow row)
    {
        ActionDraftRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<ActionDraftRevision>(row.RevisionJson, "action revision");
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

    private static bool HasNullElements(ActionDraftRevision revision) =>
        !revision.Sponsorships.IsDefault && revision.Sponsorships.Any(sponsorship => sponsorship is null);

    private static IReadOnlyList<string> DifferingFields(ActionRevisionRow row, ActionRevisionPage page)
    {
        ActionDraftRevision rebuilt;
        try
        {
            rebuilt = page.Rebuild(row);
        }
        catch (InvalidDataException)
        {
            return [ColumnsKey];
        }
        if (RevisionJsonComparison.TryParseObject(row.RevisionJson) is not { } stored) return [RevisionJsonComparison.UnreadableKey];
        var fields = RevisionJsonComparison.DifferingKeys(stored, ReviewJson.ToObject(rebuilt), NoAllowedAbsences).ToList();
        if (row.EventOn != rebuilt.EventOn()) fields.Add("eventOn");
        if (row.CosponsorCount != rebuilt.CosponsorCount()) fields.Add("cosponsorCount");
        return fields;
    }
}
