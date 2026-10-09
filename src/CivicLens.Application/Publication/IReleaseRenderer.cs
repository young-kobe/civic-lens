using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public interface IReleaseRenderer
{
    Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken);
    Task<string> RenderIndexAsync(PublicationRelease release, CancellationToken cancellationToken);

    IReadOnlyList<ReleaseAsset> Assets { get; }
}
