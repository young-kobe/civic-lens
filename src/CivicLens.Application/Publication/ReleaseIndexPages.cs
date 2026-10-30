using System.Text;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Writes the pages built from the manifest: home, all records, weekly briefs, search, report, method, about, glossary, and the issue and policy area pages.</summary>
internal sealed class ReleaseIndexPages(IReleaseDirectory releases, IReleaseRenderer renderer)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, ReleaseFacets facets,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, CancellationToken cancellationToken)
    {
        for (var page = 1; page <= PublicationProtocol.PageCount(manifest.Records.Length); page++)
            await WriteAsync(staging, PublicationProtocol.AllRecordsPagePath(page),
                await renderer.RenderAllRecordsAsync(manifest, page, cancellationToken), cancellationToken);
        var briefs = BriefsNewestFirst(manifest);
        for (var page = 1; page <= PublicationProtocol.PageCount(briefs.Length); page++)
            await WriteAsync(staging, PublicationProtocol.BriefsPagePath(page),
                await renderer.RenderBriefsAsync(manifest, briefs, page, cancellationToken), cancellationToken);
        foreach (var facet in facets.All.Where(facet => facet.HasPages))
            for (var page = 1; page <= facet.PageCount; page++)
                await WriteAsync(staging, facet.PagePath(page),
                    await renderer.RenderFacetPageAsync(facet, page, cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.FacetsPath, await renderer.RenderFacetsAsync(facets, cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.SearchPagePath, await renderer.RenderSearchAsync(manifest, facets, cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.ReportPagePath, await renderer.RenderReportAsync(cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.MethodPagePath, await renderer.RenderMethodAsync(cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.AboutPagePath, await renderer.RenderAboutAsync(cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.GlossaryPagePath, await renderer.RenderGlossaryAsync(cancellationToken), cancellationToken);
        var brief = await LoadLatestBriefAsync(briefs.FirstOrDefault(), additions, previous, cancellationToken);
        await WriteAsync(staging, PublicationProtocol.HomePath,
            await renderer.RenderHomeAsync(manifest, brief, facets, cancellationToken), cancellationToken);
    }

    private static PublishedRecordEntry[] BriefsNewestFirst(PublicationRelease manifest) =>
        [.. ReleaseRecordOrder.NewestFirst(manifest.Records.Where(entry => entry.Kind == PublishedRecordKind.Brief),
            ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId)];

    private static Task WriteAsync(IReleaseStaging staging, string path, string html, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);

    private async Task<PublishedBrief?> LoadLatestBriefAsync(PublishedRecordEntry? latest, IReadOnlyList<PendingRecord> additions,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        if (latest is null) return null;
        var brief = await ReleaseRecordFiles.ReadAsync<PublishedBrief>(releases, latest, additions, previous, cancellationToken);
        brief.Validate();
        return brief;
    }
}
