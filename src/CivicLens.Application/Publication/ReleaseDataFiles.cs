using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Writes the data files built from the manifest: the officials with a published record, and the issue and policy area names.</summary>
internal sealed class ReleaseDataFiles(VoteRecordLoader votes, PublicationOfficials officials)
{
    public async Task<PublishedOfficials> WriteAsync(IReleaseStaging staging, PublicationRelease manifest, ReleaseFacets facets,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        var ids = await PublishedOfficialIdsAsync(manifest, cancellationToken);
        var published = await officials.ResolveAsync(ids, previous.Officials, cancellationToken);
        var officialsJson = JsonSerializer.SerializeToUtf8Bytes(published, PublicationProtocol.JsonOptions);
        if (officialsJson.Length > PublishedOfficials.MaximumBytes)
            throw new ArgumentException("The officials file would exceed its size limit.", nameof(manifest));
        await staging.WriteFileAsync(PublicationProtocol.OfficialsPath, officialsJson, cancellationToken);
        await WriteAsync(staging, PublicationProtocol.CatalogPath, CatalogOf(facets), cancellationToken);
        return published;
    }

    private async Task<HashSet<string>> PublishedOfficialIdsAsync(PublicationRelease manifest, CancellationToken cancellationToken)
    {
        var ids = manifest.Records.SelectMany(entry => entry.Officials).Select(official => official.Id).ToHashSet(StringComparer.Ordinal);
        ids.UnionWith(await votes.MemberPersonIdsAsync(manifest.Records, cancellationToken));
        return ids;
    }

    private static PublishedCatalog CatalogOf(ReleaseFacets facets)
    {
        var catalog = new PublishedCatalog { Issues = EntriesOf(facets.Issues), PolicyAreas = EntriesOf(facets.PolicyAreas) };
        catalog.Validate();
        return catalog;
    }

    private static PublishedCatalogEntry[] EntriesOf(IEnumerable<ReleaseFacet> facets) =>
        [.. facets.Select(facet => new PublishedCatalogEntry { Id = facet.Id, Name = facet.Name })];

    private static Task WriteAsync<TFile>(IReleaseStaging staging, string path, TFile file, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions), cancellationToken);
}
