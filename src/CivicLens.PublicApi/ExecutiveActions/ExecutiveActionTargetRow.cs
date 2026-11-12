using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>
/// One typed target of an executive action, from the view <c>public_api.executive_action_targets</c>. The kind and the number are as stored.
/// <c>Reference</c> is the cited text of the disposition note, and it is null when the stored span does not hold the reference.
/// <c>TargetRecordId</c> is null unless exactly one other executive action of the active release has the kind and the number.
/// </summary>
public sealed record ExecutiveActionTargetRow(int Position, PublishedExecutiveActionRelation Relation, PublishedExecutiveActionKind Kind, string Number, string? Reference, string? TargetRecordId);
