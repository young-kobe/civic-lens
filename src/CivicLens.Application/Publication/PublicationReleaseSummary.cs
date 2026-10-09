namespace CivicLens.Application.Publication;

public sealed record PublicationReleaseSummary(int ReleaseNumber, string DirectoryName,
    DateTimeOffset PublishedAtUtc, int RecordCount);
