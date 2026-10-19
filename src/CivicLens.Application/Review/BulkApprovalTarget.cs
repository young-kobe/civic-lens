namespace CivicLens.Application.Review;

public sealed record BulkApprovalTarget(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion);
