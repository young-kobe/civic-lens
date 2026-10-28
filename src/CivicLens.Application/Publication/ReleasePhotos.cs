using CivicLens.Application.Collection;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Application.Roster;
using CivicLens.Core.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging;

namespace CivicLens.Application.Publication;

/// <summary>
/// Decides the photo of each official and writes the photo files. A photo that the previous release holds under the same path is
/// linked, and its capture is not read. A new photo reads its capture once, and two officials with one capture share one file.
/// A new photo that cannot be read keeps the photo of the previous profile, else the official has no photo. Both cases log an error.
/// </summary>
public sealed class ReleasePhotos(ICollectionAttemptStore attempts, ICaptureBodyReader bodies, string artifactRoot,
    ILogger<ReleasePhotos> logger)
{
    internal async Task<ReleasePhotoFiles> LoadAsync(IReadOnlyList<OfficialPhoto> rows, PreviousRelease previous,
        CancellationToken cancellationToken)
    {
        var inPrevious = previous.Officials.Values.Select(profile => profile.Photo?.Path).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var profiles = new Dictionary<string, PublishedPhoto>(StringComparer.Ordinal);
        var newFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var unreadable = new Dictionary<string, Exception>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var photo = ToPublished(row);
            if (!inPrevious.Contains(photo.Path) && !newFiles.ContainsKey(photo.Path) && !unreadable.ContainsKey(photo.Path))
            {
                try
                {
                    newFiles[photo.Path] = await ReadAsync(row, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
                {
                    unreadable[photo.Path] = exception;
                }
            }
            if (unreadable.TryGetValue(photo.Path, out var failure))
                photo = KeptPhoto(row, previous, failure);
            if (photo is not null) profiles[row.PersonId] = photo;
        }
        return new(profiles, newFiles);
    }

    internal async Task WriteAsync(IReleaseStaging staging, PublishedOfficials published, ReleasePhotoFiles photos, PreviousRelease previous,
        CancellationToken cancellationToken)
    {
        foreach (var path in published.Officials.Select(profile => profile.Photo?.Path).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            if (photos.NewFiles.TryGetValue(path, out var body))
                await staging.WriteFileAsync(path, body, cancellationToken);
            else
                await staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
        }
    }

    private static PublishedPhoto ToPublished(OfficialPhoto row) => new()
    {
        Path = PublicationProtocol.PhotoPath(row.CaptureSha256),
        Credit = row.Credit,
        SourceUrl = row.SourceUrl,
        Width = MemberPhotoImage.Width,
        Height = MemberPhotoImage.Height
    };

    private PublishedPhoto? KeptPhoto(OfficialPhoto row, PreviousRelease previous, Exception exception)
    {
        var kept = previous.Officials.GetValueOrDefault(row.PersonId)?.Photo;
        logger.LogError(exception, "The new photo of {PersonId} from attempt {AttemptId} could not be read. {Outcome}.", row.PersonId, row.AttemptId,
            kept is null ? "The release has no photo of the official" : "The release keeps the photo of the previous release");
        return kept;
    }

    private async Task<byte[]> ReadAsync(OfficialPhoto row, CancellationToken cancellationToken)
    {
        var stored = await attempts.GetAsync(row.AttemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new InvalidDataException("The photo attempt has no captured response.");
        var body = await bodies.ReadAsync(captured, artifactRoot, cancellationToken);
        return MemberPhotoImage.IsValid(captured.Response.ContentType, body)
            ? body
            : throw new InvalidDataException("The photo is not a JPEG of the published size.");
    }
}
