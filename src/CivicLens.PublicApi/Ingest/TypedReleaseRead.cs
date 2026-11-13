using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Records;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// One read of the active release inside one REPEATABLE READ snapshot. The release, its list, the entries and the texts all come from that snapshot.
/// One statement reads the release and its list. The batch read runs one query for each view, over the revisions that the ingest asks for.
/// </summary>
public sealed class TypedReleaseRead : IReleaseRead
{
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction snapshot;

    private TypedReleaseRead(NpgsqlConnection connection, NpgsqlTransaction snapshot)
    {
        this.connection = connection;
        this.snapshot = snapshot;
    }

    public SourceRelease Release { get; private set; } = null!;

    /// <summary>Opens the snapshot and reads the release and its list. Null means that no release is active. The snapshot closes when the read returns null or fails.</summary>
    public static async Task<TypedReleaseRead?> OpenAsync(RecordSnapshot snapshots, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshots.OpenAsync(cancellationToken);
        var read = new TypedReleaseRead(connection, transaction);
        try
        {
            if (await read.ReadReleaseAsync(cancellationToken) is { } release)
            {
                read.Release = release;
                return read;
            }
        }
        catch
        {
            await read.DisposeAsync();
            throw;
        }
        await read.DisposeAsync();
        return null;
    }

    private async Task<SourceRelease?> ReadReleaseAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.release_number, r.published_at_utc_ticks, a.kind, a.record_id, a.revision_number
              FROM public_api.active_release r
              LEFT JOIN public_api.active_records a ON a.release_number = r.release_number
             ORDER BY a.kind, a.record_id
            """, connection, snapshot);
        var records = new List<ListedRecord>();
        int? number = null;
        var published = default(DateTimeOffset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            number = reader.GetInt32(0);
            published = new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero);
            if (reader.IsDBNull(2)) continue;
            if (!RecordKinds.TryParse(reader.GetString(2), out var kind)) throw new InvalidDataException("The active records hold an unknown kind.");
            records.Add(new(kind, reader.GetString(3), reader.GetInt32(4)));
        }
        return number is { } release ? new(release, published, records) : null;
    }

    public async Task<RecordBatch> ReadRecordsAsync(IReadOnlyCollection<ListedRecord> wanted, CancellationToken cancellationToken)
    {
        var found = await new TypedEntryReader(snapshot).ReadAsync(new RevisionKeys(wanted), Release.ReleaseNumber, cancellationToken);
        var texts = new TypedTextReader(snapshot);
        var byKind = new Dictionary<PublishedRecordKind, Dictionary<string, RecordTexts>>();
        foreach (var kind in wanted.Select(record => record.Kind).Distinct())
            byKind[kind] = await texts.ReadAsync(kind, new RevisionKeys([.. wanted.Where(record => record.Kind == kind)]), cancellationToken);

        var records = new List<IndexedRecord>();
        var failures = new List<RecordFailure>();
        foreach (var record in wanted)
            try
            {
                records.Add(Build(record, found, byKind[record.Kind]));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new(record.RecordId, exception.Message));
            }
        return new(records, failures);
    }

    public async ValueTask DisposeAsync()
    {
        await snapshot.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static IndexedRecord Build(ListedRecord listed, Dictionary<(string, int), PublishedRecordEntry> entries, Dictionary<string, RecordTexts> texts)
    {
        if (!entries.TryGetValue((listed.RecordId, listed.RevisionNumber), out var entry) || entry.Kind != listed.Kind || !texts.TryGetValue(listed.RecordId, out var found))
            throw new InvalidDataException("The record is not in the typed views.");
        if (found.Problem is not null) throw new InvalidDataException(found.Problem);
        entry.Validate();
        return RecordReader.Finish(RecordReader.Create(entry, found.Summary, found.Source, found.Snippet), entry.BillLine);
    }
}
