using System.Text;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Writes the pages built from the manifest: home, all records, and the issue and policy area pages.</summary>
internal sealed class ReleaseIndexPages(IReleaseDirectory releases, IReleaseRenderer renderer, PublicationCatalog catalog)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, IReadOnlyList<PendingRecord> additions,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        var facets = ReleaseFacets.From(manifest, catalog);
        for (var page = 1; page <= PublicationProtocol.PageCount(manifest.Records.Length); page++)
            await WriteAsync(staging, PublicationProtocol.AllRecordsPagePath(page),
                await renderer.RenderAllRecordsAsync(manifest, page, cancellationToken), cancellationToken);
        foreach (var facet in facets.All.Where(facet => facet.HasPages))
            for (var page = 1; page <= facet.PageCount; page++)
                await WriteAsync(staging, facet.PagePath(page),
                    await renderer.RenderFacetPageAsync(facet, page, cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.FacetsPath, await renderer.RenderFacetsAsync(facets, cancellationToken), cancellationToken);
        var brief = await LoadLatestBriefAsync(manifest, additions, previous, cancellationToken);
        await WriteAsync(staging, PublicationProtocol.HomePath,
            await renderer.RenderHomeAsync(manifest, brief, facets, cancellationToken), cancellationToken);
    }

    private static Task WriteAsync(IReleaseStaging staging, string path, string html, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);

    private async Task<PublishedBrief?> LoadLatestBriefAsync(PublicationRelease manifest, IReadOnlyList<PendingRecord> additions,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        var latest = manifest.Records.Where(entry => entry.Kind == PublishedRecordKind.Brief)
            .MaxBy(entry => entry.EventDate);
        if (latest is null) return null;
        var json = additions.FirstOrDefault(addition => addition.Entry.Kind == latest.Kind && addition.RecordId == latest.RecordId)?.Json
            ?? await releases.ReadFileAsync(previous.DirectoryName!, PublicationProtocol.RecordDataPath(latest.RecordId),
                PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
        var brief = JsonSerializer.Deserialize<PublishedBrief>(json, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("A brief file is empty.");
        brief.Validate();
        return brief;
    }
}
