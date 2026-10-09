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

    public Task<string> RenderIndexAsync(PublicationRelease release, int page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        release.Validate();
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
