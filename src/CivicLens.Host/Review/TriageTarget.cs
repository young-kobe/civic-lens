namespace CivicLens.Host.Review;

public sealed record TriageTarget(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion);
