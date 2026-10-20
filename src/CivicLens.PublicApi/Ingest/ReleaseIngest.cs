using System.Text.Json;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>Stores one newer release in one transaction. A repeat of a stored release changes nothing.</summary>
public sealed class ReleaseIngest(ReleaseFeed feed, NpgsqlDataSource database)
{
    public async Task<IngestResult> RunAsync(CancellationToken cancellationToken)
    {
        var release = await feed.ReadChangedManifestAsync(cancellationToken);
        if (release is null) return IngestResult.NotModified;

        await using var index = await IndexTransaction.BeginAsync(database, cancellationToken);
        var previous = await index.LatestReleaseAsync(cancellationToken);
        if (previous >= release.ReleaseNumber)
        {
            feed.KeepValidators();
            return new(IngestOutcome.NotNewer, release.ReleaseNumber, 0, 0, []);
        }

        await index.AddReleaseAsync(release, cancellationToken);
        var stored = await index.StoredRevisionsAsync(cancellationToken);
        var listed = release.Records.Select(entry => (entry.RecordId, Revision: entry.RevisionNumber)).ToHashSet();
        var reopened = listed.Where(key => stored.TryGetValue(key, out var current) && !current).ToArray();
        var dropped = stored.Where(item => item.Value && !listed.Contains(item.Key)).Select(item => item.Key).ToArray();
        await index.ReopenAsync(reopened, cancellationToken);
        await index.CloseAsync(dropped, previous.GetValueOrDefault(), cancellationToken);

        var failures = new List<RecordFailure>();
        var indexed = 0;
        foreach (var entry in release.Records.Where(entry => !stored.ContainsKey((entry.RecordId, entry.RevisionNumber))))
        {
            var record = await TryReadAsync(entry, failures, cancellationToken);
            if (record is null) continue;
            await index.InsertAsync(record, release.ReleaseNumber, cancellationToken);
            indexed++;
        }
        await index.CommitAsync(cancellationToken);
        feed.KeepValidators();
        return new(IngestOutcome.Ingested, release.ReleaseNumber, indexed, dropped.Length, failures);
    }

    private async Task<IndexedRecord?> TryReadAsync(PublishedRecordEntry entry, List<RecordFailure> failures,
        CancellationToken cancellationToken)
    {
        var json = await feed.ReadRecordAsync(entry.RecordId, cancellationToken);
        try
        {
            return RecordReader.Read(entry, json);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            failures.Add(new(entry.RecordId, exception.Message));
            return null;
        }
    }
}
