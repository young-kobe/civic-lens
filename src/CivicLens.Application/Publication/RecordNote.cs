using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record RecordNote(PublishedRecordKind Kind, string RecordId, string Note)
{
    public PublishedRecordReference Reference => new(Kind, RecordId);
}
