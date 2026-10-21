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
        foreach (var carried in await CarriedToRenderAsync(links, build, additions, cancellationToken))
        {
            var json = await releases.ReadFileAsync(build.Previous.DirectoryName!, PublicationProtocol.RecordDataPath(carried.RecordId),
                PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
            pages[carried.RecordId] = await loaders.Carried(carried.Kind).RenderCarriedAsync(carried.RecordId, json, links, build, cancellationToken);
        }
        return pages;
    }

    // A carried page changes when a link of its related panel changes, or when its bill link points at an action that this publish withdraws or returns.
    private async Task<IEnumerable<PublishedRecordReference>> CarriedToRenderAsync(ReleaseLinks links, PublicationBuild build,
        IReadOnlyList<PendingRecord> additions, CancellationToken cancellationToken)
    {
        var skipped = additions.Select(addition => (addition.Entry.Kind, addition.RecordId))
            .Concat(build.Removed.Select(record => (record.Kind, record.RecordId))).ToHashSet();
        var returnedActions = additions.Where(addition => addition.Entry.Kind == PublishedRecordKind.Action && build.Previous.IsWithdrawn(addition.RecordId))
            .Select(addition => addition.RecordId);
        var votesOnChangedBills = await loaders.Votes.FindOnBillsOfActionsAsync(
            build.Removed.Where(record => record.Kind == PublishedRecordKind.Action).Select(record => record.RecordId).Concat(returnedActions),
            cancellationToken);
        return links.ChangedCitations.Select(cited => new PublishedRecordReference(cited.Kind, cited.RecordId))
            .Concat(build.ReleaseRecords.Where(record => record.Kind == PublishedRecordKind.Vote && votesOnChangedBills.Contains(record.RecordId))
                .Select(record => new PublishedRecordReference(record.Kind, record.RecordId)))
            .Where(record => !skipped.Contains((record.Kind, record.RecordId))).Distinct();
    }
}
