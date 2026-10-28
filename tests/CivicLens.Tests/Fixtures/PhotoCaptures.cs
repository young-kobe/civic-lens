using CivicLens.Application.Collection;
using CivicLens.Application.Publication;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Fixtures;

/// <summary>Holds the image bytes of photo attempts and counts each capture body read.</summary>
internal sealed class PhotoCaptures : ICollectionAttemptStore, ICaptureBodyReader
{
    private const string SourceUrl = "https://example.gov/images/photo.jpg";

    private readonly Dictionary<string, byte[]> images = [];

    private readonly HashSet<string> corrupt = [];

    public List<string> Reads { get; } = [];

    public CapturingLogger<ReleasePhotos> Log { get; } = new();

    public static ReleasePhotos None() => new(new PhotoCaptures(), new PhotoCaptures(), "artifacts", new CapturingLogger<ReleasePhotos>());

    public ReleasePhotos Photos(string artifactRoot) => new(this, this, artifactRoot, Log);

    public void Add(string attemptId, byte[] image) => images[attemptId] = image;

    public void AddCorrupt(string attemptId)
    {
        images[attemptId] = [];
        corrupt.Add(attemptId);
    }

    public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
        Task.FromResult(images.ContainsKey(attemptId)
            ? new StoredCollectionAttempt(new CapturedAttemptResult(attemptId, "photos", SourceUrl, SourceUrl, DateTimeOffset.UnixEpoch,
                new CollectionResponse(200, null, null, "image/jpeg", []), new CaptureIdentity(new string('a', 64), 1)), null, null)
            : null);

    public Task<byte[]> ReadAsync(CapturedAttemptResult attempt, string artifactRoot, CancellationToken cancellationToken)
    {
        Reads.Add(attempt.AttemptId);
        if (corrupt.Contains(attempt.AttemptId)) throw new InvalidDataException("Stored capture does not match its immutable identity.");
        return Task.FromResult(images[attempt.AttemptId]);
    }

    public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
