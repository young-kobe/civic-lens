using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Home;

/// <summary>A count and newest signed record for one executive action kind in the active release. <c>OldestSignedOn</c> is the first signed day that the count covers.</summary>
public sealed record HomeExecutiveGroup(PublishedExecutiveActionKind Kind, int Count, string NewestRecordId, string NewestTitle, DateOnly NewestSignedOn, DateOnly OldestSignedOn);
