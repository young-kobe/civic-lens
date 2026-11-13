using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Makes the active release of the views the active release of the index, in one transaction.
/// A release that is stored already only becomes active again. That is how the service follows a rollback of publication.
/// </summary>
public sealed class ReleaseIngest(IReleaseSource source, NpgsqlDataSource database)
{
    public async Task<IngestResult> RunAsync(CancellationToken cancellationToken)
    {
        await using var read = await source.ReadActiveAsync(cancellationToken);
        if (read is null) return IngestResult.NotModified;

        var release = read.Release;
        await using var index = await IndexTransaction.BeginAsync(database, cancellationToken);
        var result = await index.HasReleaseAsync(release.ReleaseNumber, cancellationToken)
            ? await ActivateAsync(index, release.ReleaseNumber, cancellationToken)
            : await IngestAsync(index, read, cancellationToken);
        await index.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<IngestResult> ActivateAsync(IndexTransaction index, int number, CancellationToken cancellationToken)
    {
        var moved = await index.ActiveReleaseAsync(cancellationToken) != number;
        if (moved) await index.SetActiveReleaseAsync(number, cancellationToken);
        return new(moved ? IngestOutcome.Activated : IngestOutcome.AlreadyActive, number, 0, 0, []);
    }

    private static async Task<IngestResult> IngestAsync(IndexTransaction index, IReleaseRead read, CancellationToken cancellationToken)
    {
        var release = read.Release;
        var previous = await index.LatestReleaseAsync(cancellationToken);
        if (previous > release.ReleaseNumber)
            throw new InvalidDataException($"Release {release.ReleaseNumber} is older than the newest stored release and was never stored.");

        // The records are read before the index writes, so the snapshot of the source stays open for as short a time as possible.
        var stored = await index.StoredRevisionsAsync(cancellationToken);
        var missing = release.Records.Where(record => !stored.ContainsKey((record.RecordId, record.RevisionNumber))).ToArray();
        var batch = await read.ReadRecordsAsync(missing, cancellationToken);

        await index.AddReleaseAsync(release, cancellationToken);
        var listed = release.Records.Select(record => (record.RecordId, Revision: record.RevisionNumber)).ToHashSet();
        var returning = listed.Where(key => stored.TryGetValue(key, out var open) && !open).ToArray();
        var dropped = stored.Where(item => item.Value && !listed.Contains(item.Key)).Select(item => item.Key).ToArray();
        await index.ReturnAsync(returning, release.ReleaseNumber, cancellationToken);
        await index.CloseAsync(dropped, previous.GetValueOrDefault(), cancellationToken);
        foreach (var record in batch.Records) await index.InsertAsync(record, release.ReleaseNumber, cancellationToken);
        await index.SetActiveReleaseAsync(release.ReleaseNumber, cancellationToken);
        return new(IngestOutcome.Ingested, release.ReleaseNumber, batch.Records.Count, dropped.Length, batch.Failures);
    }
}
