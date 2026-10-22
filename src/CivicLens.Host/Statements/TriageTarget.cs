namespace CivicLens.Host.Statements;

public sealed record TriageTarget(string RecordId, int ExpectedRevisionNumber, int ExpectedReviewStateVersion);
