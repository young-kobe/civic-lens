using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Host.Publication;

/// <param name="publicApiUrl">The address of the search service. The search page needs it, and says that search is not available without it.</param>
public sealed class ReleaseRenderer(string? publicApiUrl = null) : IReleaseRenderer
{
    public const string PublicApiUrlVariable = "CIVIC_LENS_PUBLIC_API_URL";

    private static readonly ServiceProvider Services = new ServiceCollection().BuildServiceProvider();

    public IReadOnlyList<ReleaseAsset> Assets { get; } = PublicAssets.Load();

    public Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedRecordPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedRecordPage.Record)] = record
        });
    }

    public Task<string> RenderStatementAsync(PublishedStatement record, ImmutableArray<PublishedRelationship> related,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedStatementPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedStatementPage.Record)] = record,
            [nameof(PublishedStatementPage.Related)] = related
        });
    }

    public Task<string> RenderActionAsync(PublishedAction record, ImmutableArray<PublishedRelationship> related,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedActionPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedActionPage.Record)] = record,
            [nameof(PublishedActionPage.Related)] = related
        });
    }

    public Task<string> RenderVoteAsync(PublishedVote record, string? billActionRecordId, ImmutableArray<PublishedRelationship> related,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedVotePage>(new Dictionary<string, object?>
        {
            [nameof(PublishedVotePage.Record)] = record,
            [nameof(PublishedVotePage.BillActionRecordId)] = billActionRecordId,
            [nameof(PublishedVotePage.Related)] = related
        });
    }

    public Task<string> RenderRelationshipAsync(PublishedRelationship record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedRelationshipPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedRelationshipPage.Record)] = record
        });
    }

    public Task<string> RenderBriefAsync(PublishedBrief record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedBriefPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedBriefPage.Record)] = record
        });
    }

    public Task<string> RenderHomeAsync(PublicationRelease release, PublishedBrief? brief, ReleaseFacets facets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(facets);
        brief?.Validate();
        return RenderAsync<PublishedHomePage>(new Dictionary<string, object?>
        {
            [nameof(PublishedHomePage.Release)] = release,
            [nameof(PublishedHomePage.Brief)] = brief,
            [nameof(PublishedHomePage.Facets)] = facets
        });
    }

    public Task<string> RenderAllRecordsAsync(PublicationRelease release, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, PublicationProtocol.PageCount(release.Records.Length));
        return RenderAsync<PublishedAllRecordsPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedAllRecordsPage.Release)] = release,
            [nameof(PublishedAllRecordsPage.Page)] = page
        });
    }

    public Task<string> RenderFacetPageAsync(ReleaseFacet facet, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facet);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, facet.PageCount);
        return RenderAsync<PublishedFacetPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedFacetPage.Facet)] = facet,
            [nameof(PublishedFacetPage.Page)] = page
        });
    }

    public Task<string> RenderFacetsAsync(ReleaseFacets facets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facets);
        return RenderAsync<PublishedFacetsPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedFacetsPage.Facets)] = facets
        });
    }

    public Task<string> RenderOfficialAsync(ReleaseOfficialPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Profile.Validate();
        return RenderAsync<PublishedOfficialPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedOfficialPage.Page)] = page
        });
    }

    public Task<string> RenderOfficialsAsync(IReadOnlyList<OfficialListing> officials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(officials);
        return RenderAsync<PublishedOfficialsPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedOfficialsPage.Officials)] = officials
        });
    }

    public static ReleaseRenderer FromEnvironment() => new(Environment.GetEnvironmentVariable(PublicApiUrlVariable)?.TrimEnd('/') is { Length: > 0 } url ? url : null);

    public Task<string> RenderSearchAsync(PublicationRelease release, ReleaseFacets facets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(facets);
        return RenderAsync<PublishedSearchPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedSearchPage.Release)] = release,
            [nameof(PublishedSearchPage.Facets)] = facets,
            [nameof(PublishedSearchPage.PublicApiUrl)] = publicApiUrl
        });
    }

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(Services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
