using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The first time that a record reached the public site. The store keeps it once and never changes it.</summary>
public sealed record PublishedFirstPublication(PublishedRecordKind Kind, string RecordId, DateTimeOffset FirstPublishedAtUtc);
