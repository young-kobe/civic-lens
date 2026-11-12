using CivicLens.PublicApi.Ingest;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.PublicApi;

/// <summary>The release JSON feed as a release source. It is the oracle that the typed reader must equal, and it feeds the tests that still publish through <see cref="FakeReleaseSite"/>.</summary>
internal sealed class FeedReleaseSource(ReleaseFeed feed) : IReleaseSource
{
    private readonly Dictionary<(string RecordId, int Revision), PublishedRecordEntry> listed = [];

    public async Task<SourceRelease?> ReadChangedAsync(CancellationToken cancellationToken)
    {
        var manifest = await feed.ReadChangedManifestAsync(cancellationToken);
        if (manifest is null) return null;
        listed.Clear();
        foreach (var entry in manifest.Records) listed[(entry.RecordId, entry.RevisionNumber)] = entry;
        return new(manifest.ReleaseNumber, manifest.PublishedAtUtc, [.. manifest.Records.Select(entry => new ListedRecord(entry.Kind, entry.RecordId, entry.RevisionNumber))]);
    }

    public async Task<RecordBatch> ReadRecordsAsync(SourceRelease release, IReadOnlyCollection<ListedRecord> wanted, CancellationToken cancellationToken)
    {
        var records = new List<IndexedRecord>();
        var failures = new List<RecordFailure>();
        foreach (var record in wanted)
        {
            var json = await feed.ReadRecordAsync(record.RecordId, cancellationToken);
            try
            {
                records.Add(RecordReader.Read(listed[(record.RecordId, record.RevisionNumber)], json));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new(record.RecordId, exception.Message));
            }
        }
        return new(records, failures);
    }

    public void Stored() => feed.KeepValidators();
}
