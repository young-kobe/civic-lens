using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record ReviewStateExpectation(PublishedRecordKind Kind, string RecordId, int RevisionNumber, int ReviewStateVersion);
