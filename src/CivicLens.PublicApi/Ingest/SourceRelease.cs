using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Ingest;

/// <summary>One record revision that a release lists.</summary>
public sealed record ListedRecord(PublishedRecordKind Kind, string RecordId, int RevisionNumber);

/// <summary>The release that the source offers for ingest, with the revisions it lists.</summary>
public sealed record SourceRelease(int ReleaseNumber, DateTimeOffset PublishedAtUtc, IReadOnlyList<ListedRecord> Records);

/// <summary>The index documents that a source read, and the records that it could not read. A failed record never stops its release.</summary>
public sealed record RecordBatch(IReadOnlyList<IndexedRecord> Records, IReadOnlyList<RecordFailure> Failures);
