using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Renders every page that this publish writes. A page lists the related records of the new release.</summary>
internal sealed class ReleasePages(IReleaseDirectory releases, PublishedRecordLoaders loaders)
{
    public async Task<IReadOnlyDictionary<string, string>> RenderAsync(PublicationBuild build, IReadOnlyList<PendingRecord> additions,
        CancellationToken cancellationToken)
    {
        var links = await ReleaseLinks.LoadAsync(releases, build, loaders.Relationships, additions, cancellationToken);
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var addition in additions)
            pages[addition.RecordId] = await addition.RenderPage(links.Citing(addition.Entry.Kind, addition.RecordId), cancellationToken);
        foreach (var cited in CarriedCitedBy(links, additions, build.Removed))
        {
            var json = await releases.ReadFileAsync(build.Previous.DirectoryName!, PublicationProtocol.RecordDataPath(cited.RecordId),
                PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
            pages[cited.RecordId] = await loaders.Carried(cited.Kind).RenderCarriedAsync(cited.RecordId, json, links, build, cancellationToken);
        }
        return pages;
    }

    private static IEnumerable<PublishedRelationshipCitation> CarriedCitedBy(ReleaseLinks links, IReadOnlyList<PendingRecord> additions,
        ImmutableArray<PublishedRecordReference> removed)
    {
        var skipped = additions.Select(addition => (addition.Entry.Kind, addition.RecordId))
            .Concat(removed.Select(record => (record.Kind, record.RecordId))).ToHashSet();
        return links.ChangedCitations.Where(cited => !skipped.Contains((cited.Kind, cited.RecordId)))
            .DistinctBy(cited => (cited.Kind, cited.RecordId));
    }
}
