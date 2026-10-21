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

    internal const string ReleaseRecordColumns = """
        COALESCE(e ->> 'kind', 'documentChange') AS kind, COALESCE(e ->> 'recordId', e ->> 'draftId') AS record_id,
        (e ->> 'revisionNumber')::int AS revision_number
        """;

    public const string ActivePublishedRecords = $"""
        active AS (
            SELECT {ReleaseRecordColumns}
            FROM publication_releases r CROSS JOIN LATERAL jsonb_array_elements(r.records_json::jsonb) e
            WHERE r.is_active)
        """;

    private const string ReleaseRecords = $$"""
        SELECT kind AS "Kind", record_id AS "RecordId", revision_number AS "RevisionNumber" FROM (
            SELECT {{ReleaseRecordColumns}}
            FROM publication_releases r CROSS JOIN LATERAL jsonb_array_elements(r.records_json::jsonb) e
            WHERE r.release_number = {0}) records
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
        SELECT revision_number AS "Value" FROM active WHERE lower(kind) = lower({0}) AND record_id = {1}
        """;

    public static Task<int?> ReadActiveRevisionNumberAsync(CollectionAttemptDbContext db, PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<int?>(ActiveRevisionNumber, kind.ToString(), recordId).SingleOrDefaultAsync(cancellationToken);

    private static ImmutableArray<PublishedRevisionBinding> ToBindings(IEnumerable<ReleaseRecordRow> rows) =>
        [.. rows.Select(row => new PublishedRevisionBinding(Enum.Parse<PublishedRecordKind>(row.Kind, ignoreCase: true),
            row.RecordId, row.RevisionNumber))];

    // Published state comes from the active release, not the draft, so this stays a read query.
    public const string UnpublishedApprovedDrafts = $"""
        WITH {ActivePublishedRecords}
        SELECT d.draft_id AS "RecordId", d.current_revision_number AS "CurrentRevisionNumber",
               d.created_at_utc_ticks AS "CreatedAtUtcTicks", a.revision_number AS "PublishedRevisionNumber"
        FROM document_change_drafts d
        LEFT JOIN active a ON a.kind = 'documentChange' AND a.record_id = d.draft_id
        WHERE d.review_status = 'Approved'
        """;

    // The latest decision of a record in the Approved status is the approval of the current revision.
    public static string CodeApprovedOnly(string unpublishedApprovedSql, string decisionTable) => $"""
        {unpublishedApprovedSql}
          AND EXISTS (SELECT 1 FROM {decisionTable} c
                       WHERE c.record_id = d.record_id AND c.review_state_version = d.review_state_version
                         AND c.decision_json::jsonb ->> 'actorSubject' = '{ReviewAuthor.CodeSubject}')
        """;

    private sealed class ReleaseRecordRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
