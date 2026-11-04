namespace CivicLens.Application.Publication;

public sealed record SkippedPublishedEntry(string Kind, string RecordId, int RevisionNumber, string Reason);
