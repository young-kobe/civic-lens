using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Writes the data files built from the manifest: the officials with a published record, and the issue and policy area names.</summary>
internal sealed class ReleaseDataFiles(IReleaseDirectory releases, PublicationOfficials officials)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, ReleaseFacets facets,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var ids = await PublishedOfficialIdsAsync(manifest, additions, previous, cancellationToken);
        await WriteAsync(staging, PublicationProtocol.OfficialsPath, await officials.ResolveAsync(ids, cancellationToken), cancellationToken);
        await WriteAsync(staging, PublicationProtocol.CatalogPath, CatalogOf(facets), cancellationToken);
    }

    private async Task<HashSet<string>> PublishedOfficialIdsAsync(PublicationRelease manifest, IReadOnlyList<PendingRecord> additions,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        var ids = manifest.Records.SelectMany(entry => entry.Officials).Select(official => official.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in manifest.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote))
        {
            var vote = await ReleaseRecordFiles.ReadAsync<PublishedVote>(releases, entry, additions, previous, cancellationToken);
            vote.Validate();
            ids.UnionWith(vote.Members.Select(member => member.Official.Id));
        }
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
