using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public interface IReleaseRenderer
{
    Task<string> RenderRecordAsync(PublishedDocumentChange record, PublicationCatalog catalog, CancellationToken cancellationToken);
    Task<string> RenderStatementAsync(PublishedStatement record, ImmutableArray<PublishedRelationship> related, PublicationCatalog catalog,
        CancellationToken cancellationToken);
    Task<string> RenderActionAsync(PublishedAction record, PublishedBillStatus? billStatus, ImmutableArray<PublishedRelationship> related,
        PublicationCatalog catalog, CancellationToken cancellationToken);
    Task<string> RenderVoteAsync(PublishedVote record, string? billActionRecordId, ImmutableArray<PublishedRelationship> related,
        PublicationCatalog catalog, CancellationToken cancellationToken);
    Task<string> RenderExecutiveActionAsync(PublishedExecutiveAction record, CancellationToken cancellationToken);
    Task<string> RenderRelationshipAsync(PublishedRelationship record, PublicationCatalog catalog, CancellationToken cancellationToken);
    Task<string> RenderBriefAsync(PublishedBrief record, CancellationToken cancellationToken);
    Task<string> RenderWithdrawalAsync(PublishedWithdrawal withdrawal, CancellationToken cancellationToken);
    Task<string> RenderHomeAsync(PublicationRelease release, PublishedBrief? brief, ReleaseFacets facets, HomeHighlights highlights, CancellationToken cancellationToken);
    Task<string> RenderAllRecordsAsync(PublicationRelease release, int page, CancellationToken cancellationToken);
    Task<string> RenderBriefsAsync(PublicationRelease release, PublishedRecordEntry[] briefs, int page, CancellationToken cancellationToken);
    Task<string> RenderFacetPageAsync(ReleaseFacet facet, int page, CancellationToken cancellationToken);
    Task<string> RenderFacetsAsync(ReleaseFacets facets, CancellationToken cancellationToken);
    Task<string> RenderOfficialAsync(ReleaseOfficialPage page, PublicationCatalog catalog, CancellationToken cancellationToken);
    Task<string> RenderOfficialsAsync(IReadOnlyList<OfficialListing> officials, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken);
    Task<string> RenderReportAsync(CancellationToken cancellationToken);
    Task<string> RenderMethodAsync(CancellationToken cancellationToken);
    Task<string> RenderAboutAsync(CancellationToken cancellationToken);
    Task<string> RenderGlossaryAsync(CancellationToken cancellationToken);
    Task<string> RenderSearchAsync(PublicationRelease release, ReleaseFacets facets, CancellationToken cancellationToken);

    Task<string> RenderNotFoundAsync(CancellationToken cancellationToken);

    IReadOnlyList<ReleaseAsset> Assets { get; }

    PublicBaseUrl? PublicBaseUrl { get; }
}
