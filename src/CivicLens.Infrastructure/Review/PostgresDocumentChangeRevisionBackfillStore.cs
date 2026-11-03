using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

public sealed class PostgresDocumentChangeRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string ColumnsKey = "columns";
    private static readonly HashSet<string> NoAllowedAbsences = [];

    private readonly RevisionBackfill<DocumentChangeRevisionRow, DocumentChangeDraftRevision, DocumentChangeRevisionPage> backfill = new(contextFactory, new(
        "document_change_revision_backfill", PostgresAdvisoryLock.DocumentChangeReview, row => row.Headline == null,
        DocumentChangeRevisionRows.ReadPageAsync, FillColumnsAsync, ReadMovable, DifferingFields, nameof(DocumentChangeRevisionRow.DraftId)));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static Task FillColumnsAsync(CollectionAttemptDbContext db, DocumentChangeRevisionRow row, DocumentChangeDraftRevision revision,
        CancellationToken cancellationToken)
    {
        DocumentChangeRevisionRows.FillColumns(row, revision);
        return Task.CompletedTask;
    }

    private static (DocumentChangeDraftRevision? Revision, string? Refusal) ReadMovable(DocumentChangeRevisionRow row)
    {
        DocumentChangeDraftRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<DocumentChangeDraftRevision>(row.RevisionJson, "document change revision");
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

    private static bool HasNullElements(DocumentChangeDraftRevision revision) =>
        !revision.OfficialIds.IsDefault && revision.OfficialIds.Any(id => id is null) || !revision.IssueIds.IsDefault && revision.IssueIds.Any(id => id is null) ||
        !revision.Citations.IsDefault && revision.Citations.Any(citation => citation is null);

    private static IReadOnlyList<string> DifferingFields(DocumentChangeRevisionRow row, DocumentChangeRevisionPage page)
    {
        DocumentChangeDraftRevision rebuilt;
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
