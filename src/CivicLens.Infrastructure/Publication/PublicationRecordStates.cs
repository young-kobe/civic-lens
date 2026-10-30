using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublicationRecordStates
{
    private const string CloseRanges = """
        UPDATE publication_record_states s SET last_release = {0}
        FROM unnest({1}::text[], {2}::text[]) AS k(kind, record_id)
        WHERE s.kind = k.kind AND s.record_id = k.record_id AND s.last_release IS NULL
        """;

    private const string OpenRanges = """
        INSERT INTO publication_record_states (kind, record_id, revision_number, first_release)
        SELECT k.kind, k.record_id, k.revision_number, {0} FROM unnest({1}::text[], {2}::text[], {3}::int[]) AS k(kind, record_id, revision_number)
        """;

    private const string LockReleases = "LOCK TABLE publication_releases IN SHARE ROW EXCLUSIVE MODE";

    private const string ExpectedRanges = """
        expected AS (
            WITH positions AS (
                SELECT release_number, row_number() OVER (ORDER BY release_number) AS position, records_json
                FROM publication_releases),
            entries AS (
                SELECT DISTINCT p.release_number, p.position,
                    COALESCE(e ->> 'kind', 'documentChange') AS kind, COALESCE(e ->> 'recordId', e ->> 'draftId') AS record_id,
                    (e ->> 'revisionNumber')::int AS revision_number
                FROM positions p CROSS JOIN LATERAL jsonb_array_elements(p.records_json::jsonb) e),
            islands AS (
                SELECT *, position - row_number() OVER (PARTITION BY kind, record_id, revision_number ORDER BY position) AS island
                FROM entries)
            SELECT kind, record_id, revision_number, min(release_number) AS first_release,
                CASE WHEN max(release_number) = (SELECT max(release_number) FROM publication_releases) THEN NULL
                    ELSE max(release_number) END AS last_release
            FROM islands
            GROUP BY kind, record_id, revision_number, island)
        """;

    private const string Columns = "kind, record_id, revision_number, first_release, last_release";

    private const string Drifted = $"""
        WITH {ExpectedRanges}
        SELECT EXISTS (
            (SELECT {Columns} FROM expected EXCEPT SELECT {Columns} FROM publication_record_states)
            UNION ALL
            (SELECT {Columns} FROM publication_record_states EXCEPT SELECT {Columns} FROM expected)) AS "Value"
        """;

    private const string Rebuild = $"""
        DELETE FROM publication_record_states;
        INSERT INTO publication_record_states ({Columns})
        WITH {ExpectedRanges}
        SELECT {Columns} FROM expected
        """;

    // An old binary that runs during a rollback commits releases without table rows, so the table can fall behind records_json.
    public static async Task RebuildWhenDriftedAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(LockReleases, cancellationToken);
        if (await db.Database.SqlQueryRaw<bool>(Drifted).SingleAsync(cancellationToken))
            await db.Database.ExecuteSqlRawAsync(Rebuild, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task RecordAsync(CollectionAttemptDbContext db, PublicationCommit commit, CancellationToken cancellationToken)
    {
        var highest = commit.ReleaseNumber - 1;
        var before = await db.Set<PublicationRecordStateRow>().AsNoTracking()
            .Where(row => row.LastRelease == null)
            .ToDictionaryAsync(row => (row.Kind, row.RecordId), row => row.RevisionNumber, cancellationToken);
        var after = commit.Records.ToDictionary(record => (Kind: PublishedRecordReference.NameOf(record.Kind), record.RecordId),
            record => record.RevisionNumber);

        var closed = before.Where(entry => !Matches(after, entry)).Select(entry => entry.Key).ToArray();
        var opened = after.Where(entry => !Matches(before, entry)).ToArray();
        if (closed.Length > 0)
            await db.Database.ExecuteSqlRawAsync(CloseRanges,
                [highest, closed.Select(key => key.Kind).ToArray(), closed.Select(key => key.RecordId).ToArray()], cancellationToken);
        if (opened.Length > 0)
            await db.Database.ExecuteSqlRawAsync(OpenRanges, [commit.ReleaseNumber, opened.Select(entry => entry.Key.Kind).ToArray(),
                opened.Select(entry => entry.Key.RecordId).ToArray(), opened.Select(entry => entry.Value).ToArray()], cancellationToken);
    }

    private static bool Matches(Dictionary<(string Kind, string RecordId), int> revisions, KeyValuePair<(string Kind, string RecordId), int> entry) =>
        revisions.TryGetValue(entry.Key, out var revision) && revision == entry.Value;
}
