namespace CivicLens.Application.Publication;

public sealed record PublicationReleaseList(IReadOnlyList<PublicationReleaseSummary> Releases, string? ActiveDirectoryName);
