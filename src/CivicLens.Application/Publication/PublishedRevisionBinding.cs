using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublishedRevisionBinding(PublishedRecordKind Kind, string RecordId, int RevisionNumber)
{
    public static PublishedRevisionBinding Of(PublishedRecordEntry entry) => new(entry.Kind, entry.RecordId, entry.RevisionNumber);
}
