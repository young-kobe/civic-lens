namespace CivicLens.Application.Review;

public sealed record SkippedRevision(string RecordId, int RevisionNumber, string Reason);
