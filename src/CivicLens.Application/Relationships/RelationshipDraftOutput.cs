namespace CivicLens.Application.Relationships;

internal sealed record RelationshipDraftOutput(string Type, string Reason, string? StatementQuote, string? TargetFactId);
