namespace CivicLens.Application.Statements;

public sealed record CreateStatementDraftRequest(string CandidateId, string IdempotencyKey);
