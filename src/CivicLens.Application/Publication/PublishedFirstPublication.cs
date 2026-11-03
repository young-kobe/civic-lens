using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublishedFirstPublication(PublishedRecordKind Kind, string RecordId, DateTimeOffset FirstPublishedAtUtc);
