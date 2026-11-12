using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>One record revision that a release lists.</summary>
public sealed record ListedRecord(PublishedRecordKind Kind, string RecordId, int RevisionNumber);

/// <summary>The release that the source offers for ingest, with the revisions it lists.</summary>
public sealed record SourceRelease(int ReleaseNumber, DateTimeOffset PublishedAtUtc, IReadOnlyList<ListedRecord> Records);

/// <summary>The index documents that a source read, and the records that it could not read. A failed record never stops its release.</summary>
public sealed record RecordBatch(IReadOnlyList<IndexedRecord> Records, IReadOnlyList<RecordFailure> Failures);

/// <summary>The boundary of the ingest to the published data. <see cref="TypedReleaseSource"/> reads the <c>public_api</c> views.</summary>
public interface IReleaseSource
{
    /// <summary>Gives the release to ingest. Null means that nothing changed since the last stored release.</summary>
    Task<SourceRelease?> ReadChangedAsync(CancellationToken cancellationToken);

    /// <summary>Reads the index documents of the given revisions of the release.</summary>
    Task<RecordBatch> ReadRecordsAsync(SourceRelease release, IReadOnlyCollection<ListedRecord> wanted, CancellationToken cancellationToken);

    /// <summary>Tells the source that the release is stored, so it may skip an unchanged release next time.</summary>
    void Stored();
}
