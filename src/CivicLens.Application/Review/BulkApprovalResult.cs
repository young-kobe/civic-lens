namespace CivicLens.Application.Review;

public enum BulkApprovalOutcome { Approved, Conflict, Rejected, NotFound }

public sealed record BulkApprovalResult(string RecordId, BulkApprovalOutcome Outcome);
