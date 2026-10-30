using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Host.Publication;

/// <param name="publicApiUrl">The address of the search service. The search page and the report page need it, and say that they are not available without it.</param>
/// <param name="turnstileSiteKey">The public Turnstile site key. The report page needs it, and says that reporting is not available without it.</param>
/// <param name="publicBaseUrl">The public origin of the site. Without it, pages have no canonical address and a release has no sitemap, robots file, or not found page.</param>
public sealed class ReleaseRenderer(string? publicApiUrl, string? turnstileSiteKey, PublicBaseUrl? publicBaseUrl = null) : IReleaseRenderer
{
    public const string PublicApiUrlVariable = "CIVIC_LENS_PUBLIC_API_URL";
    public const string TurnstileSiteKeyVariable = "CIVIC_LENS_TURNSTILE_SITE_KEY";
    public const string PublicBaseUrlVariable = "CIVIC_LENS_PUBLIC_BASE_URL";

    private readonly ServiceProvider services = PageServices(publicBaseUrl);

    public IReadOnlyList<ReleaseAsset> Assets { get; } = PublicAssets.Load();

    public PublicBaseUrl? PublicBaseUrl => publicBaseUrl;

    public Task<string> RenderRecordAsync(PublishedDocumentChange record, PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedRecordPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedRecordPage.Record)] = record,
            [nameof(PublishedRecordPage.Catalog)] = catalog
        });
    }

    public Task<string> RenderStatementAsync(PublishedStatement record, ImmutableArray<PublishedRelationship> related,
        PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedStatementPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedStatementPage.Record)] = record,
            [nameof(PublishedStatementPage.Related)] = related,
            [nameof(PublishedStatementPage.Catalog)] = catalog
        });
    }

    public Task<string> RenderActionAsync(PublishedAction record, ImmutableArray<PublishedRelationship> related,
        PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedActionPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedActionPage.Record)] = record,
            [nameof(PublishedActionPage.Related)] = related,
            [nameof(PublishedActionPage.Catalog)] = catalog
        });
    }

    public Task<string> RenderVoteAsync(PublishedVote record, string? billActionRecordId, ImmutableArray<PublishedRelationship> related,
        PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedVotePage>(new Dictionary<string, object?>
        {
            [nameof(PublishedVotePage.Record)] = record,
            [nameof(PublishedVotePage.BillActionRecordId)] = billActionRecordId,
            [nameof(PublishedVotePage.Related)] = related,
            [nameof(PublishedVotePage.Catalog)] = catalog
        });
    }

    public Task<string> RenderExecutiveActionAsync(PublishedExecutiveAction record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedExecutiveActionPage>(new Dictionary<string, object?> { [nameof(PublishedExecutiveActionPage.Record)] = record });
    }

    public Task<string> RenderRelationshipAsync(PublishedRelationship record, PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        record.Validate();
        return RenderAsync<PublishedRelationshipPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedRelationshipPage.Record)] = record,
            [nameof(PublishedRelationshipPage.Catalog)] = catalog
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

    public Task<string> RenderWithdrawalAsync(PublishedWithdrawal withdrawal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withdrawal);
        withdrawal.Validate();
        return RenderAsync<PublishedWithdrawalPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedWithdrawalPage.Withdrawal)] = withdrawal
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
            [nameof(PublishedAllRecordsPage.Page)] = page,
            [nameof(PublishedAllRecordsPage.PublicApiUrl)] = publicApiUrl
        });
    }

    public Task<string> RenderBriefsAsync(PublicationRelease release, PublishedRecordEntry[] briefs, int page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(briefs);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, PublicationProtocol.PageCount(briefs.Length));
        return RenderAsync<PublishedBriefsPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedBriefsPage.Release)] = release,
            [nameof(PublishedBriefsPage.Briefs)] = briefs,
            [nameof(PublishedBriefsPage.Page)] = page
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
            [nameof(PublishedFacetPage.Page)] = page,
            [nameof(PublishedFacetPage.PublicApiUrl)] = publicApiUrl
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

    public Task<string> RenderOfficialAsync(ReleaseOfficialPage page, PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Profile.Validate();
        return RenderAsync<PublishedOfficialPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedOfficialPage.Page)] = page,
            [nameof(PublishedOfficialPage.Catalog)] = catalog
        });
    }

    public Task<string> RenderOfficialsAsync(IReadOnlyList<OfficialListing> officials, DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(officials);
        return RenderAsync<PublishedOfficialsPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedOfficialsPage.Officials)] = officials,
            [nameof(PublishedOfficialsPage.PublishedAtUtc)] = publishedAtUtc
        });
    }

    public static ReleaseRenderer FromEnvironment() => new(
        Environment.GetEnvironmentVariable(PublicApiUrlVariable)?.TrimEnd('/') is { Length: > 0 } url ? url : null,
        Environment.GetEnvironmentVariable(TurnstileSiteKeyVariable)?.Trim() is { Length: > 0 } siteKey ? siteKey : null,
        PublicBaseUrlFromEnvironment());

    public static PublicBaseUrl? PublicBaseUrlFromEnvironment() =>
        PublicBaseUrl.Parse(Environment.GetEnvironmentVariable(PublicBaseUrlVariable), PublicBaseUrlVariable);

    public Task<string> RenderReportAsync(CancellationToken cancellationToken) =>
        RenderAsync<PublishedReportPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedReportPage.PublicApiUrl)] = publicApiUrl,
            [nameof(PublishedReportPage.TurnstileSiteKey)] = turnstileSiteKey
        });

    public Task<string> RenderMethodAsync(CancellationToken cancellationToken) =>
        RenderAsync<PublishedMethodPage>(new Dictionary<string, object?>());

    public Task<string> RenderAboutAsync(CancellationToken cancellationToken) =>
        RenderAsync<PublishedAboutPage>(new Dictionary<string, object?>());

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

    public Task<string> RenderNotFoundAsync(CancellationToken cancellationToken)
    {
        if (publicBaseUrl is null) throw new InvalidOperationException($"The not found page needs {PublicBaseUrlVariable}.");
        return RenderAsync<PublishedNotFoundPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedNotFoundPage.RootPath)] = publicBaseUrl.Join(string.Empty)
        });
    }

    private static ServiceProvider PageServices(PublicBaseUrl? publicBaseUrl)
    {
        var services = new ServiceCollection();
        if (publicBaseUrl is not null) services.AddSingleton(publicBaseUrl);
        return services.BuildServiceProvider();
    }

    private async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }
}
