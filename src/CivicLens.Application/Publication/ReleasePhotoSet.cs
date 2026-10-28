using CivicLens.Application.Roster;

namespace CivicLens.Application.Publication;

/// <summary>The newest photo of each configured official, and the byte limit that the photo source sets for one photo.</summary>
internal sealed record ReleasePhotoSet(IReadOnlyList<OfficialPhoto> Photos, long MaximumBytes)
{
    public static ReleasePhotoSet None { get; } = new([], 0);

    public IReadOnlyDictionary<string, OfficialPhoto> ByPerson { get; } = Photos.ToDictionary(photo => photo.PersonId, StringComparer.Ordinal);
}
