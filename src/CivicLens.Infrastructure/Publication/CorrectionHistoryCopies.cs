using System.Data;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class CorrectionHistoryCopies
{
    private const int BatchSize = 256;
    private const int EncodingVersion = PublishedCorrectionHistoryIdentity.CurrentVersion;

    public static async Task<IReadOnlyList<CorrectionHistoryBinding>> PrepareAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublicationCorrectionHistory> histories, CancellationToken cancellationToken)
    {
        var prepared = histories.Select(Prepare).ToArray();
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        foreach (var distinct in Deduplicate(prepared).OrderBy(history => history.HistoryId, StringComparer.Ordinal).Chunk(BatchSize))
        {
            await InsertBodiesAsync(db, distinct, cancellationToken);
            await RequireBodiesMatchAsync(db, distinct, cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return prepared.Select(item => new CorrectionHistoryBinding(item.Record, item.HistoryId)).ToArray();
    }

    public static async Task<IReadOnlyDictionary<PublishedRecordReference, StoredCorrectionHistory>> ReadAsync(CollectionAttemptDbContext db,
        int releaseNumber, IReadOnlyCollection<PublishedRevisionBinding> records, CancellationToken cancellationToken)
    {
        var keys = Keys(records);
        if (keys.Length == 0) return new Dictionary<PublishedRecordReference, StoredCorrectionHistory>();
        if (records.Select(record => (record.Kind, record.RecordId)).Distinct().Count() != records.Count)
            throw new InvalidDataException("A release cannot contain multiple revisions of the same correction history record.");
        var rows = new List<HistoryBindingRow>();
        foreach (var batch in keys.Chunk(BatchSize))
            rows.AddRange(await db.Database.SqlQueryRaw<HistoryBindingRow>("""
                SELECT k.kind AS "Kind", k.record_id AS "RecordId", k.revision_number AS "RequestedRevisionNumber",
                       h.revision_number AS "StoredRevisionNumber", h.history_id AS "HistoryId",
                       c.encoding_version AS "EncodingVersion", c.correction_count AS "CorrectionCount"
                  FROM unnest({0}::text[], {1}::text[], {2}::int[]) AS k(kind, record_id, revision_number)
                  LEFT JOIN release_correction_histories h ON h.release_number = {3} AND h.kind = k.kind AND h.record_id = k.record_id
                  LEFT JOIN published_correction_histories c ON c.history_id = h.history_id
                """, batch.Select(key => key.Kind).ToArray(), batch.Select(key => key.RecordId).ToArray(),
                batch.Select(key => key.RevisionNumber).ToArray(), releaseNumber).ToArrayAsync(cancellationToken));
        foreach (var row in rows)
        {
            if (row.StoredRevisionNumber is { } storedRevision && storedRevision != row.RequestedRevisionNumber)
                throw new InvalidDataException("A release correction history is bound to a different record revision.");
            if (row.StoredRevisionNumber is not null && (row.HistoryId is null || row.EncodingVersion is null || row.CorrectionCount is null))
                throw new InvalidDataException("A release correction history references a missing content header.");
        }
        rows = rows.Where(row => row.StoredRevisionNumber is not null).ToList();
        var historyIds = rows.Select(row => row.HistoryId!).Distinct(StringComparer.Ordinal).ToArray();
        var entries = await ReadEntriesAsync(db, historyIds, cancellationToken);
        var decoded = new Dictionary<string, PublishedCorrection[]>(StringComparer.Ordinal);
        foreach (var row in rows.GroupBy(row => row.HistoryId!, StringComparer.Ordinal).Select(group => group.First()))
        {
            var corrections = Reconstruct(row, entries.GetValueOrDefault(row.HistoryId!, []));
            if (PublishedCorrectionHistoryIdentity.Of(corrections, row.EncodingVersion!.Value) != row.HistoryId)
                throw new InvalidDataException("A stored correction history does not match its content identity.");
            decoded.Add(row.HistoryId!, corrections);
        }
        var result = new Dictionary<PublishedRecordReference, StoredCorrectionHistory>();
        foreach (var row in rows)
        {
            var binding = new PublishedRevisionBinding(PublishedEntries.KindOf(row.Kind), row.RecordId, row.RequestedRevisionNumber);
            var corrections = decoded[row.HistoryId!];
            PublishedCorrection.ValidateAll(corrections, binding.RevisionNumber, binding.Kind);
            result.Add(new PublishedRecordReference(binding.Kind, binding.RecordId), new StoredCorrectionHistory(
                new CorrectionHistoryBinding(binding, row.HistoryId!), (PublishedCorrection[])corrections.Clone()));
        }
        return result;
    }

    public static async Task BindAsync(CollectionAttemptDbContext db, int releaseNumber,
        IReadOnlyCollection<PublishedRevisionBinding> records, IReadOnlyCollection<CorrectionHistoryBinding> bindings,
        CancellationToken cancellationToken)
    {
        var recordKeys = Keys(records).ToHashSet();
        foreach (var binding in bindings) ValidateRecord(binding.Record);
        var bindingKeys = bindings.Select(binding => Key(binding.Record)).ToArray();
        if (recordKeys.Count != bindings.Count || bindingKeys.Distinct().Count() != bindingKeys.Length || !recordKeys.SetEquals(bindingKeys))
            throw new InvalidDataException("Correction history bindings must exactly cover the release records once.");
        if (bindings.Any(binding => !IsHistoryId(binding.HistoryId)))
            throw new InvalidDataException("A correction history binding has an invalid content identity.");
        if (bindings.Count == 0) return;

        var kinds = bindings.Select(binding => PublishedRecordReference.NameOf(binding.Record.Kind)).ToArray();
        var recordIds = bindings.Select(binding => binding.Record.RecordId).ToArray();
        var revisions = bindings.Select(binding => binding.Record.RevisionNumber).ToArray();
        var historyIds = bindings.Select(binding => binding.HistoryId).ToArray();
        var matched = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
              FROM unnest({0}::text[], {1}::text[], {2}::int[], {3}::text[]) AS k(kind, record_id, revision_number, history_id)
              JOIN published_entries e ON e.kind = k.kind AND e.record_id = k.record_id AND e.revision_number = k.revision_number
              JOIN published_correction_histories h ON h.history_id = k.history_id
             WHERE NOT EXISTS (
                   SELECT 1 FROM published_correction_history_entries c
                    WHERE c.history_id = h.history_id AND c.replaced_revision_number >= k.revision_number)
            """, kinds, recordIds, revisions, historyIds).SingleAsync(cancellationToken);
        if (matched != bindings.Count)
            throw new InvalidDataException("A correction history binding does not reference a published entry and prepared history.");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO release_correction_histories (release_number, kind, record_id, revision_number, history_id)
            SELECT {0}, k.kind, k.record_id, k.revision_number, k.history_id
              FROM unnest({1}::text[], {2}::text[], {3}::int[], {4}::text[]) AS k(kind, record_id, revision_number, history_id)
            """, new object[] { releaseNumber, kinds, recordIds, revisions, historyIds }, cancellationToken);
    }

    public static async Task<(int Added, int AlreadyPresent)> BindBootstrapAsync(CollectionAttemptDbContext db, int releaseNumber,
        IReadOnlyCollection<CorrectionHistoryBinding> bindings, CancellationToken cancellationToken)
    {
        var records = bindings.Select(binding => binding.Record).ToArray();
        var existing = await ReadAsync(db, releaseNumber, records, cancellationToken);
        foreach (var binding in bindings)
        {
            if (existing.TryGetValue(new PublishedRecordReference(binding.Record.Kind, binding.Record.RecordId), out var stored) &&
                stored.Binding.HistoryId != binding.HistoryId)
                throw new InvalidDataException("A correction history bootstrap conflicts with an existing binding.");
        }
        var missing = bindings.Where(binding => !existing.ContainsKey(new PublishedRecordReference(binding.Record.Kind, binding.Record.RecordId))).ToArray();
        if (missing.Length > 0) await BindAsync(db, releaseNumber, missing.Select(binding => binding.Record).ToArray(), missing, cancellationToken);
        return (missing.Length, bindings.Count - missing.Length);
    }

    private static PreparedHistory Prepare(PublicationCorrectionHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        ValidateRecord(history.Record);
        if (history.Corrections is null) throw new InvalidDataException("A correction history cannot be null.");
        var corrections = (PublishedCorrection[])history.Corrections.Clone();
        PublishedCorrection.ValidateAll(corrections, history.Record.RevisionNumber, history.Record.Kind);
        return new PreparedHistory(history.Record, corrections, PublishedCorrectionHistoryIdentity.Of(corrections));
    }

    private static void ValidateRecord(PublishedRevisionBinding record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.RevisionNumber < 1 || !PublicationProtocol.IsRecordId(record.Kind, record.RecordId))
            throw new InvalidDataException("A correction history requires a valid published record revision.");
    }

    private static async Task InsertBodiesAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PreparedHistory> histories,
        CancellationToken cancellationToken)
    {
        var ordered = histories.OrderBy(history => history.HistoryId, StringComparer.Ordinal).ToArray();
        var entries = ordered.SelectMany(history => history.Corrections.Select((correction, position) => new
        {
            history.HistoryId,
            Position = checked((short)position),
            correction.ReplacedRevisionNumber,
            UtcTicks = correction.PublishedAtUtc.UtcTicks,
            OffsetMinutes = checked((short)correction.PublishedAtUtc.Offset.TotalMinutes),
            correction.Note,
            correction.FactsUnchanged
        })).ToArray();
        await db.Database.ExecuteSqlRawAsync("""
            WITH inserted AS (
                INSERT INTO published_correction_histories (history_id, encoding_version, correction_count)
                SELECT k.history_id, {0}, k.correction_count
                  FROM unnest({1}::text[], {2}::smallint[]) AS k(history_id, correction_count)
                 ORDER BY k.history_id
                ON CONFLICT DO NOTHING
                RETURNING history_id)
            INSERT INTO published_correction_history_entries
                (history_id, position, replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, facts_unchanged)
            SELECT inserted.history_id, k.position, k.replaced_revision_number, k.utc_ticks, k.offset_minutes, k.note, k.facts_unchanged
              FROM inserted
              JOIN unnest({3}::text[], {4}::smallint[], {5}::int[], {6}::bigint[], {7}::smallint[], {8}::text[], {9}::boolean[])
                AS k(history_id, position, replaced_revision_number, utc_ticks, offset_minutes, note, facts_unchanged)
                ON k.history_id = inserted.history_id
            """, new object[] { EncodingVersion, ordered.Select(history => history.HistoryId).ToArray(),
                ordered.Select(history => (short)history.Corrections.Length).ToArray(), entries.Select(entry => entry.HistoryId).ToArray(),
                entries.Select(entry => entry.Position).ToArray(), entries.Select(entry => entry.ReplacedRevisionNumber).ToArray(),
                entries.Select(entry => entry.UtcTicks).ToArray(), entries.Select(entry => entry.OffsetMinutes).ToArray(),
                entries.Select(entry => entry.Note).ToArray(), entries.Select(entry => entry.FactsUnchanged).ToArray() }, cancellationToken);
    }

    private static async Task RequireBodiesMatchAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PreparedHistory> histories,
        CancellationToken cancellationToken)
    {
        var rows = await ReadBodyRowsAsync(db, histories.Select(history => history.HistoryId).ToArray(), cancellationToken);
        foreach (var history in histories)
        {
            var header = rows.Headers.SingleOrDefault(row => row.HistoryId == history.HistoryId);
            if (header is null || header.EncodingVersion != EncodingVersion || header.CorrectionCount != history.Corrections.Length)
                throw new InvalidDataException("A stored correction history header conflicts with its content identity.");
            var stored = Reconstruct(header, rows.Entries.GetValueOrDefault(history.HistoryId, []));
            if (!Same(stored, history.Corrections) || PublishedCorrectionHistoryIdentity.Of(stored, header.EncodingVersion) != history.HistoryId)
                throw new InvalidDataException("A stored correction history body conflicts with its content identity.");
        }
    }

    private static async Task<IReadOnlyDictionary<string, PublishedCorrectionHistoryEntryRow[]>> ReadEntriesAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<string> historyIds, CancellationToken cancellationToken)
    {
        var entries = new List<PublishedCorrectionHistoryEntryRow>();
        foreach (var batch in historyIds.Chunk(BatchSize))
            entries.AddRange(await db.Set<PublishedCorrectionHistoryEntryRow>().AsNoTracking().Where(row => batch.Contains(row.HistoryId))
                .OrderBy(row => row.HistoryId).ThenBy(row => row.Position).ToArrayAsync(cancellationToken));
        return entries.GroupBy(entry => entry.HistoryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
    }

    private static async Task<BodyRows> ReadBodyRowsAsync(CollectionAttemptDbContext db, IReadOnlyCollection<string> historyIds,
        CancellationToken cancellationToken)
    {
        if (historyIds.Count == 0) return new([], new Dictionary<string, PublishedCorrectionHistoryEntryRow[]>(StringComparer.Ordinal));
        var headers = new List<PublishedCorrectionHistoryRow>();
        var entries = new List<PublishedCorrectionHistoryEntryRow>();
        foreach (var batch in historyIds.Chunk(BatchSize))
        {
            headers.AddRange(await db.Set<PublishedCorrectionHistoryRow>().AsNoTracking().Where(row => batch.Contains(row.HistoryId))
                .ToArrayAsync(cancellationToken));
            entries.AddRange(await db.Set<PublishedCorrectionHistoryEntryRow>().AsNoTracking().Where(row => batch.Contains(row.HistoryId))
                .OrderBy(row => row.HistoryId).ThenBy(row => row.Position).ToArrayAsync(cancellationToken));
        }
        return new(headers.ToArray(), entries.GroupBy(entry => entry.HistoryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal));
    }

    private static PublishedCorrection[] Reconstruct(PublishedCorrectionHistoryRow header, PublishedCorrectionHistoryEntryRow[] rows)
    {
        if (header.EncodingVersion != EncodingVersion || rows.Length != header.CorrectionCount ||
            rows.Where((row, index) => row.Position != index).Any())
            throw new InvalidDataException("A stored correction history is incomplete or malformed.");
        return rows.Select(row => new PublishedCorrection
        {
            ReplacedRevisionNumber = row.ReplacedRevisionNumber,
            PublishedAtUtc = new DateTimeOffset(row.PublishedAtUtcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(row.PublishedOffsetMinutes)),
            Note = row.Note,
            FactsUnchanged = row.FactsUnchanged
        }).ToArray();
    }

    private static PublishedCorrection[] Reconstruct(HistoryBindingRow binding, PublishedCorrectionHistoryEntryRow[] rows)
    {
        if (binding.EncodingVersion is not { } version || binding.CorrectionCount is not { } count || version != EncodingVersion ||
            rows.Length != count || rows.Where((row, index) => row.Position != index).Any())
            throw new InvalidDataException("A stored correction history is incomplete or malformed.");
        return rows.Select(row => new PublishedCorrection
        {
            ReplacedRevisionNumber = row.ReplacedRevisionNumber,
            PublishedAtUtc = new DateTimeOffset(row.PublishedAtUtcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(row.PublishedOffsetMinutes)),
            Note = row.Note,
            FactsUnchanged = row.FactsUnchanged
        }).ToArray();
    }

    private static bool Same(PublishedCorrection[] left, PublishedCorrection[] right) => left.Length == right.Length &&
        left.Zip(right).All(pair => pair.First.ReplacedRevisionNumber == pair.Second.ReplacedRevisionNumber &&
            pair.First.PublishedAtUtc.UtcTicks == pair.Second.PublishedAtUtc.UtcTicks && pair.First.PublishedAtUtc.Offset == pair.Second.PublishedAtUtc.Offset &&
            string.Equals(pair.First.Note, pair.Second.Note, StringComparison.Ordinal) && pair.First.FactsUnchanged == pair.Second.FactsUnchanged);

    private static PreparedHistory[] Deduplicate(IReadOnlyCollection<PreparedHistory> histories)
    {
        var byId = new Dictionary<string, PreparedHistory>(StringComparer.Ordinal);
        foreach (var history in histories)
        {
            if (byId.TryGetValue(history.HistoryId, out var existing))
            {
                if (!Same(existing.Corrections, history.Corrections))
                    throw new InvalidDataException("Different correction history bodies produced the same content identity.");
                continue;
            }
            byId.Add(history.HistoryId, history);
        }
        return byId.Values.OrderBy(history => history.HistoryId, StringComparer.Ordinal).ToArray();
    }

    private static RecordKey[] Keys(IReadOnlyCollection<PublishedRevisionBinding> records)
    {
        foreach (var record in records) ValidateRecord(record);
        var keys = records.Select(Key).ToArray();
        if (keys.Distinct().Count() != keys.Length)
            throw new InvalidDataException("A release cannot contain duplicate correction history records.");
        return keys;
    }

    private static RecordKey Key(PublishedRevisionBinding record) => new(PublishedRecordReference.NameOf(record.Kind), record.RecordId, record.RevisionNumber);

    private static bool IsHistoryId(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record PreparedHistory(PublishedRevisionBinding Record, PublishedCorrection[] Corrections, string HistoryId);
    private sealed record RecordKey(string Kind, string RecordId, int RevisionNumber);
    private sealed record BodyRows(PublishedCorrectionHistoryRow[] Headers,
        IReadOnlyDictionary<string, PublishedCorrectionHistoryEntryRow[]> Entries);
    private sealed class HistoryBindingRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RequestedRevisionNumber { get; set; }
        public int? StoredRevisionNumber { get; set; }
        public string? HistoryId { get; set; }
        public int? EncodingVersion { get; set; }
        public short? CorrectionCount { get; set; }
    }
}
