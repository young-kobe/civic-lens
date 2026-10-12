namespace CivicLens.Application.Statements;

public sealed record StatementNotInReviewItem(string CandidateId, string SourceId, string Url, string Stage, string ReasonCode);
