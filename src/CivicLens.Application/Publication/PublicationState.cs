namespace CivicLens.Application.Publication;

public sealed record PublicationState(int LatestReleaseNumber, PublicationReleaseSummary? Active);
