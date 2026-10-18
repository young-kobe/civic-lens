using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Host.Publication;

public sealed class ReleaseRenderer : IReleaseRenderer
{
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

    public Task<string> RenderIndexAsync(PublicationRelease release, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, PublicationProtocol.IndexPageCount(release.Records.Length));
        return RenderAsync<PublishedIndexPage>(new Dictionary<string, object?>
        {
            [nameof(PublishedIndexPage.Release)] = release,
            [nameof(PublishedIndexPage.Page)] = page
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
