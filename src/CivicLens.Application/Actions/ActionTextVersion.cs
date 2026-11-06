namespace CivicLens.Application.Actions;

/// <summary>The counts and name of one stored bill text version, read from its row at publication. The counts are characters of that version.</summary>
public sealed record ActionTextVersion(string PackageId, string VersionType, DateOnly? VersionDate, int CharsRead, int CharsTotal);
