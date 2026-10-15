namespace CivicLens.Application.Actions;

public enum ActionApprovalOutcome { Approved, Conflict, Rejected, NotFound }

public sealed record ActionApprovalResult(string RecordId, ActionApprovalOutcome Outcome);
