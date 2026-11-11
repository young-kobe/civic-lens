namespace CivicLens.Core.ExecutiveActions;

public enum ExecutiveActionRelation { Amends, Revokes }

/// <summary>An exact numbered executive order or proclamation that disposition notes amend or revoke. Start and Length locate the reference inside the note value.</summary>
public sealed record ExecutiveActionDispositionTarget(ExecutiveActionRelation Relation, ExecutiveActionKind Kind, string Number, int Start, int Length);
