using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Core.Collection;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the photo file of each profile that has a photo. A photo that the previous release holds under the same path is linked
/// from it. Only a new photo reads its capture, so a publish needs the artifact directory only for a new photo. Two officials with
/// one image share one file.
/// </summary>
public sealed class ReleasePhotos(ICollectionAttemptStore attempts, ICaptureBodyReader bodies, string? artifactRoot)
{
    internal async Task WriteAsync(IReleaseStaging staging, PublishedOfficials published, ReleasePhotoSet photos, PreviousRelease previous,
        CancellationToken cancellationToken)
    {
        var inPrevious = previous.Officials.Values.Select(profile => profile.Photo?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, path) in published.Officials.Where(profile => profile.Photo is not null).Select(profile => (profile.Id, profile.Photo!.Path)))
        {
            if (!written.Add(path)) continue;
            if (inPrevious.Contains(path))
                await staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
            else
                await staging.WriteFileAsync(path, await ReadAsync(photos.ByPerson[id], photos.MaximumBytes, cancellationToken), cancellationToken);
        }
    }

    private async Task<byte[]> ReadAsync(OfficialPhoto photo, long maximumBytes, CancellationToken cancellationToken)
    {
        if (artifactRoot is null)
            throw new InvalidOperationException($"The new photo of {photo.PersonId} needs the artifact directory to read its capture, and no artifact directory is set.");
        var stored = await attempts.GetAsync(photo.AttemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new InvalidDataException($"The photo of {photo.PersonId} has no captured attempt.");
        var body = await bodies.ReadAsync(captured, artifactRoot, cancellationToken);
        if (body.Length > maximumBytes)
            throw new InvalidDataException($"The photo of {photo.PersonId} is larger than the {maximumBytes} byte limit of the photo source.");
        return body;
    }
}
