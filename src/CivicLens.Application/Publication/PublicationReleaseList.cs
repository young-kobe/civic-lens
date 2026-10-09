namespace CivicLens.Application.Publication;

public sealed record PublicationReleaseList(IReadOnlyList<PublicationReleaseSummary> Releases,
    int? ActiveReleaseNumber, string? ServedDirectoryName);
