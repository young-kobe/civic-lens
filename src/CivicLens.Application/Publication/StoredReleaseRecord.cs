using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>One state row that covers a release, with the first publication time and the entry row of the revision. Each is null when the table lacks it.</summary>
public sealed record StoredReleaseRecord(PublishedRecordKind Kind, string RecordId, int RevisionNumber, DateTimeOffset? FirstPublishedAtUtc,
    PublishedEntryFacts? Facts);
