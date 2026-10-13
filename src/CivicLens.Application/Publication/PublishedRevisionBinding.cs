using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublishedRevisionBinding(PublishedRecordKind Kind, string RecordId, int RevisionNumber);
