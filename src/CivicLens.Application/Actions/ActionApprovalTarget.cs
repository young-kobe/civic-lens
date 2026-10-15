namespace CivicLens.Application.Actions;

public sealed record ActionApprovalTarget(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion);
