namespace CivicLens.Application.Statements;

public sealed record StatementDraftEvidence(StatementGateEvidence Article, StatementGateCarryOver CarryOver, string? ExistingDraftId);
