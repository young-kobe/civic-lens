using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublishedFirstPublication(PublishedRecordKind Kind, string RecordId, DateTimeOffset FirstPublishedAtUtc)
{
    public static PublishedFirstPublication Of(PublishedRecordEntry entry) => new(entry.Kind, entry.RecordId, entry.FirstPublishedAtUtc);
}
