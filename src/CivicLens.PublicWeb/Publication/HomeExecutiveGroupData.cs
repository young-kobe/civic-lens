using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One executive action group of Home. <c>Count</c> counts the published documents of the kind, signed from <c>OldestSignedOn</c> to <c>NewestSignedOn</c>.</summary>
public sealed record HomeExecutiveGroupData(PublishedExecutiveActionKind Kind, int Count, string NewestRecordId, string NewestTitle, DateOnly NewestSignedOn, DateOnly OldestSignedOn);
