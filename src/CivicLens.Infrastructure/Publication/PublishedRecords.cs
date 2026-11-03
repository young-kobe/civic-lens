using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublishedRecords
{
    private const string Insert = """
        INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks)
        SELECT k.kind, k.record_id, k.ticks FROM unnest({0}::text[], {1}::text[], {2}::bigint[]) AS k(kind, record_id, ticks)
        ON CONFLICT DO NOTHING
        """;

    private const string Read = """
        SELECT p.kind AS "Kind", p.record_id AS "RecordId", p.first_published_at_utc_ticks AS "Ticks"
        FROM published_records p
        JOIN unnest({0}::text[], {1}::text[]) AS k(kind, record_id) ON p.kind = k.kind AND p.record_id = k.record_id
        """;

    public static async Task<IReadOnlyDictionary<PublishedRecordReference, DateTimeOffset>> ReadAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0) return new Dictionary<PublishedRecordReference, DateTimeOffset>();
        var rows = await db.Database.SqlQueryRaw<FirstPublishedRow>(Read,
            records.Select(record => PublishedRecordReference.NameOf(record.Kind)).ToArray(),
            records.Select(record => record.RecordId).ToArray()).ToArrayAsync(cancellationToken);
        var requested = records.ToDictionary(record => (Kind: PublishedRecordReference.NameOf(record.Kind), record.RecordId));
        return rows.ToDictionary(row => requested[(row.Kind, row.RecordId)], row => new DateTimeOffset(row.Ticks, TimeSpan.Zero));
    }

    public static async Task<int> InsertAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedFirstPublication> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0) return 0;
        return await db.Database.ExecuteSqlRawAsync(Insert, [records.Select(record => PublishedRecordReference.NameOf(record.Kind)).ToArray(),
            records.Select(record => record.RecordId).ToArray(), records.Select(record => record.FirstPublishedAtUtc.UtcTicks).ToArray()],
            cancellationToken);
    }

    public static async Task RequireStoredTimesAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedFirstPublication> records,
        CancellationToken cancellationToken)
    {
        var stored = await ReadAsync(db, [.. records.Select(record => new PublishedRecordReference(record.Kind, record.RecordId))], cancellationToken);
        if (records.Any(record => stored[new(record.Kind, record.RecordId)] != record.FirstPublishedAtUtc))
            throw new PublicationConflictException("The first publication time of a record changed after the release was built. Rebuild it.");
    }

    private sealed class FirstPublishedRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public long Ticks { get; set; }
    }
}
