namespace CivicLens.Application.Publication;

/// <param name="Record">The first record that shows the failure, as kind:id. It is null when no single record does.</param>
public sealed record FailedRelease(int ReleaseNumber, string DirectoryName, string Reason, string? Record);
