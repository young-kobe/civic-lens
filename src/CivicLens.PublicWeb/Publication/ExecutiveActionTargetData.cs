using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>A document named by this executive action, with an optional exact citation and active-release page.</summary>
public sealed record ExecutiveActionTargetData(
    PublishedExecutiveActionRelation Relation,
    PublishedExecutiveActionKind Kind,
    string Number,
    string? Reference,
    string? TargetRecordId);
