using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Reads the active release and its index documents from the <c>public_api</c> views. One statement reads the release and its records, so they cannot differ.
/// The batch read runs one query for each view, over the revisions that the ingest asks for.
/// </summary>
public sealed class TypedReleaseSource(NpgsqlDataSource views) : IReleaseSource
{
    private readonly TypedEntryReader entries = new(views);
    private readonly TypedTextReader texts = new(views);

    public async Task<SourceRelease?> ReadChangedAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("""
            SELECT r.release_number, r.published_at_utc_ticks, a.kind, a.record_id, a.revision_number
              FROM public_api.active_release r
              LEFT JOIN public_api.active_records a ON a.release_number = r.release_number
             ORDER BY a.kind, a.record_id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<ListedRecord>();
        int? number = null;
        var published = default(DateTimeOffset);
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

    public async Task<RecordBatch> ReadRecordsAsync(SourceRelease release, IReadOnlyCollection<ListedRecord> wanted, CancellationToken cancellationToken)
    {
        var keys = new RevisionKeys(wanted);
        var found = await entries.ReadAsync(keys, release.ReleaseNumber, cancellationToken);
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

    public void Stored()
    {
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
