using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Makes the release of the manifest the active release, in one transaction.
/// A release that is stored already only becomes active again. That is how the service follows a rollback of the static site.
/// </summary>
public sealed class ReleaseIngest(ReleaseFeed feed, NpgsqlDataSource database)
{
    public async Task<IngestResult> RunAsync(CancellationToken cancellationToken)
    {
        var release = await feed.ReadChangedManifestAsync(cancellationToken);
        if (release is null) return IngestResult.NotModified;

        await using var index = await IndexTransaction.BeginAsync(database, cancellationToken);
        var result = await index.HasReleaseAsync(release.ReleaseNumber, cancellationToken)
            ? await ActivateAsync(index, release.ReleaseNumber, cancellationToken)
            : await IngestAsync(index, release, cancellationToken);
        await index.CommitAsync(cancellationToken);
        feed.KeepValidators();
        return result;
    }

    private static async Task<IngestResult> ActivateAsync(IndexTransaction index, int number, CancellationToken cancellationToken)
    {
        var moved = await index.ActiveReleaseAsync(cancellationToken) != number;
        if (moved) await index.SetActiveReleaseAsync(number, cancellationToken);
        return new(moved ? IngestOutcome.Activated : IngestOutcome.AlreadyActive, number, 0, 0, []);
    }

    private async Task<IngestResult> IngestAsync(IndexTransaction index, PublicationRelease release, CancellationToken cancellationToken)
    {
        var previous = await index.LatestReleaseAsync(cancellationToken);
        if (previous > release.ReleaseNumber)
            throw new InvalidDataException($"Release {release.ReleaseNumber} is older than the newest stored release and was never stored.");

        await index.AddReleaseAsync(release, cancellationToken);
        var stored = await index.StoredRevisionsAsync(cancellationToken);
        var listed = release.Records.Select(entry => (entry.RecordId, Revision: entry.RevisionNumber)).ToHashSet();
        var returning = listed.Where(key => stored.TryGetValue(key, out var open) && !open).ToArray();
        var dropped = stored.Where(item => item.Value && !listed.Contains(item.Key)).Select(item => item.Key).ToArray();
        await index.ReturnAsync(returning, release.ReleaseNumber, cancellationToken);
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
        await index.SetActiveReleaseAsync(release.ReleaseNumber, cancellationToken);
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add(new(entry.RecordId, exception.Message));
            return null;
        }
    }
}
