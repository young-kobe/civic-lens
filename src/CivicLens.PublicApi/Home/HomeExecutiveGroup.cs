using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>A count and newest signed record for one executive action kind in the active release.</summary>
public sealed record HomeExecutiveGroup(PublishedExecutiveActionKind Kind, int Count, string NewestRecordId, string NewestTitle, DateOnly NewestSignedOn);
