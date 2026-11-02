using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewQueries
{
    public static IQueryable<DocumentComparisonRow> EligibleComparisons(CollectionAttemptDbContext db) =>
        db.Set<DocumentComparisonRow>().AsNoTracking().Where(comparison => comparison.HasChanges &&
            !db.Set<DocumentChangeDraftRow>().Any(draft => draft.ComparisonId == comparison.ComparisonId));

    private const string HeldInRelease = "s.first_release <= r.release_number AND (s.last_release IS NULL OR s.last_release >= r.release_number)";

    private const string ActivePublishedRecords = $"""
        active AS (
            SELECT s.kind, s.record_id, s.revision_number
            FROM publication_record_states s JOIN publication_releases r ON r.is_active AND {HeldInRelease})
        """;

    private const string ReleaseRecords = $$"""
        SELECT s.kind AS "Kind", s.record_id AS "RecordId", s.revision_number AS "RevisionNumber"
        FROM publication_record_states s JOIN publication_releases r ON r.release_number = {0} AND {{HeldInRelease}}
        """;

    private const string ActiveReleaseRecords = $"""
        WITH {ActivePublishedRecords}
        SELECT kind AS "Kind", record_id AS "RecordId", revision_number AS "RevisionNumber" FROM active
        """;

    public static async Task<ImmutableArray<PublishedRevisionBinding>> ReadReleaseRecordsAsync(CollectionAttemptDbContext db,
        int releaseNumber, CancellationToken cancellationToken) =>
        ToBindings(await db.Database.SqlQueryRaw<ReleaseRecordRow>(ReleaseRecords, releaseNumber).ToArrayAsync(cancellationToken));

    public static async Task<ImmutableArray<PublishedRevisionBinding>> ReadActiveReleaseRecordsAsync(CollectionAttemptDbContext db,
        CancellationToken cancellationToken) =>
        ToBindings(await db.Database.SqlQueryRaw<ReleaseRecordRow>(ActiveReleaseRecords).ToArrayAsync(cancellationToken));

    private const string ActiveRevisionNumber = $$"""
        WITH {{ActivePublishedRecords}}
        SELECT revision_number AS "Value" FROM active WHERE kind = {0} AND record_id = {1}
        """;

    public static Task<int?> ReadActiveRevisionNumberAsync(CollectionAttemptDbContext db, PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<int?>(ActiveRevisionNumber, PublishedRecordReference.NameOf(kind), recordId).SingleOrDefaultAsync(cancellationToken);

    private static ImmutableArray<PublishedRevisionBinding> ToBindings(IEnumerable<ReleaseRecordRow> rows) =>
        [.. rows.Select(row => new PublishedRevisionBinding(Enum.Parse<PublishedRecordKind>(row.Kind, ignoreCase: true),
            row.RecordId, row.RevisionNumber))];

    // Published state comes from the active release, not the draft, so this stays a read query.
    // The map must stay in a subquery. As the inner side of a join, Postgres copies the whole map for each draft.
    public static string UnpublishedApproved(string kind, string draftTable, string recordIdColumn) => $"""
        WITH {ActivePublishedRecords}
        SELECT d.{recordIdColumn} AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks",
               ((SELECT jsonb_object_agg(record_id, revision_number) FROM active WHERE kind = '{kind}') ->> d.{recordIdColumn})::int
                   AS "PublishedRevisionNumber"
        FROM {draftTable} d
        WHERE d.review_status = 'Approved'
        """;

    private const string CountApprovedOutsideActiveRelease = $"""
        WITH {ActivePublishedRecords}
        SELECT count(*)::int AS "Value" FROM (
            SELECT 'action' AS kind, record_id AS id FROM action_drafts WHERE review_status = 'Approved'
            UNION ALL SELECT 'brief', record_id FROM brief_drafts WHERE review_status = 'Approved'
            UNION ALL SELECT 'documentChange', draft_id FROM document_change_drafts WHERE review_status = 'Approved'
            UNION ALL SELECT 'executiveAction', record_id FROM executive_action_drafts WHERE review_status = 'Approved'
            UNION ALL SELECT 'relationship', record_id FROM relationship_drafts WHERE review_status = 'Approved'
            UNION ALL SELECT 'statement', candidate_id FROM statement_drafts WHERE review_status = 'Approved'
            UNION ALL SELECT 'vote', record_id FROM vote_drafts WHERE review_status = 'Approved') approved
        WHERE NOT EXISTS (SELECT 1 FROM active a WHERE a.kind = approved.kind AND a.record_id = approved.id)
        """;

    public static Task<int> CountApprovedOutsideActiveReleaseAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<int>(CountApprovedOutsideActiveRelease).SingleAsync(cancellationToken);

    public static readonly string UnpublishedApprovedDrafts = UnpublishedApproved("documentChange", "document_change_drafts", "draft_id");

    private sealed class ReleaseRecordRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
