using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public interface IReleaseRenderer
{
    Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken);
    Task<string> RenderIndexAsync(PublicationRelease release, int page, CancellationToken cancellationToken);

    IReadOnlyList<ReleaseAsset> Assets { get; }
}
