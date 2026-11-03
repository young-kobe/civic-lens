namespace CivicLens.Application.Review;

public sealed record RevisionMismatch(string RecordId, int RevisionNumber, IReadOnlyList<string> Fields);
