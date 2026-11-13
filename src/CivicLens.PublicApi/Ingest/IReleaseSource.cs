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
    /// <summary>Opens one read of the active release. Null means that no release is active.</summary>
    Task<IReleaseRead?> ReadActiveAsync(CancellationToken cancellationToken);
}

/// <summary>One open read of a release. The release, its list and its records all come from the same published state, so a later release cannot mix into them.</summary>
public interface IReleaseRead : IAsyncDisposable
{
    SourceRelease Release { get; }

    /// <summary>Reads the index documents of the given revisions of the release.</summary>
    Task<RecordBatch> ReadRecordsAsync(IReadOnlyCollection<ListedRecord> wanted, CancellationToken cancellationToken);
}
