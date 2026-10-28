using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The photo of each official that the release shows, and the bytes of each photo that the previous release lacks.</summary>
internal sealed record ReleasePhotoFiles(IReadOnlyDictionary<string, PublishedPhoto> Profiles, IReadOnlyDictionary<string, byte[]> NewFiles);
