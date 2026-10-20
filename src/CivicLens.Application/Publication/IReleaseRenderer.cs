using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public interface IReleaseRenderer
{
    Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken);
    Task<string> RenderStatementAsync(PublishedStatement record, ImmutableArray<PublishedRelationship> related,
        CancellationToken cancellationToken);
    Task<string> RenderActionAsync(PublishedAction record, ImmutableArray<PublishedRelationship> related, CancellationToken cancellationToken);
    Task<string> RenderVoteAsync(PublishedVote record, string? billActionRecordId, ImmutableArray<PublishedRelationship> related,
        CancellationToken cancellationToken);
    Task<string> RenderRelationshipAsync(PublishedRelationship record, CancellationToken cancellationToken);
    Task<string> RenderBriefAsync(PublishedBrief record, CancellationToken cancellationToken);
    Task<string> RenderHomeAsync(PublicationRelease release, PublishedBrief? brief, ReleaseFacets facets, CancellationToken cancellationToken);
    Task<string> RenderAllRecordsAsync(PublicationRelease release, int page, CancellationToken cancellationToken);
    Task<string> RenderFacetPageAsync(ReleaseFacet facet, int page, CancellationToken cancellationToken);
    Task<string> RenderFacetsAsync(ReleaseFacets facets, CancellationToken cancellationToken);
    Task<string> RenderSearchAsync(PublicationRelease release, ReleaseFacets facets, CancellationToken cancellationToken);

    IReadOnlyList<ReleaseAsset> Assets { get; }
}
