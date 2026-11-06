namespace CivicLens.Application.Statements;

/// <summary>The draft that a statement candidate already holds. A stale model draft is one that a new model revision may replace.</summary>
public sealed record StatementExistingDraft(int CurrentRevisionNumber, bool IsStaleModelDraft);
