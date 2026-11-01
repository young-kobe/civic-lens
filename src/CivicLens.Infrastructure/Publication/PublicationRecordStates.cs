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
        await RequireOneRowForEachRecordAsync(db, commit, cancellationToken);
    }

    private static async Task RequireOneRowForEachRecordAsync(CollectionAttemptDbContext db, PublicationCommit commit,
        CancellationToken cancellationToken)
    {
        var rows = await db.Set<PublicationRecordStateRow>().AsNoTracking()
            .CountAsync(row => row.FirstRelease <= commit.ReleaseNumber && (row.LastRelease == null || row.LastRelease >= commit.ReleaseNumber),
                cancellationToken);
        if (rows != commit.Records.Length)
            throw new PublicationStateMismatchException(
                $"Release {commit.ReleaseNumber} lists {commit.Records.Length} records, but the state table holds {rows} rows for it.");
    }

    private static bool Matches(Dictionary<(string Kind, string RecordId), int> revisions, KeyValuePair<(string Kind, string RecordId), int> entry) =>
        revisions.TryGetValue(entry.Key, out var revision) && revision == entry.Value;
}
