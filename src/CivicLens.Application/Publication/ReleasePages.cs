using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Renders every page that this publish writes. A page lists the related records of the new release.</summary>
internal sealed class ReleasePages(IReleaseDirectory releases, PublishedRecordLoaders loaders)
{
    public async Task<IReadOnlyDictionary<string, string>> RenderAsync(PublicationBuild build, IReadOnlyList<PendingRecord> additions,
        CancellationToken cancellationToken)
    {
        var changedWithoutLinks = await CarriedWithChangedFactsAsync(build, additions, cancellationToken);
        var links = await ReleaseLinks.LoadAsync(releases, build, loaders.Relationships, additions, changedWithoutLinks, cancellationToken);
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var addition in additions)
            pages[addition.RecordId] = await addition.RenderPage(links.Citing(addition.Entry.Kind, addition.RecordId), cancellationToken);
        foreach (var carried in CarriedToRender(links, changedWithoutLinks, build, additions))
        {
            var json = await releases.ReadFileAsync(build.Previous.DirectoryName!, PublicationProtocol.RecordDataPath(carried.RecordId),
                PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
            pages[carried.RecordId] = await loaders.Carried(carried.Kind).RenderCarriedAsync(carried.RecordId, json, links, build, cancellationToken);
        }
        return pages;
    }

    // A carried page changes when a link of its related panel changes, when its bill link points at an action that this publish withdraws or returns,
    // or when its bill status changed. The links of these pages must load too, so the Related panel stays.
    private async Task<IReadOnlyList<PublishedRecordReference>> CarriedWithChangedFactsAsync(PublicationBuild build,
        IReadOnlyList<PendingRecord> additions, CancellationToken cancellationToken)
    {
        var returnedActions = build.Returned.Where(record => record.Kind == PublishedRecordKind.Action).Select(record => record.RecordId);
        var votesOnChangedBills = await loaders.Votes.FindOnBillsOfActionsAsync(
            build.Removed.Where(record => record.Kind == PublishedRecordKind.Action).Select(record => record.RecordId).Concat(returnedActions),
            cancellationToken);
        var changedStatuses = ReleaseBillStatuses.ChangedActionIds(
            ReleaseBillStatuses.ActionIdsOf(build.ReleaseRecords), build.Previous, build.BillStatuses);
        var skipped = SkippedRecords(build, additions);
        return [.. changedStatuses.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))
            .Concat(build.ReleaseRecords.Where(record => record.Kind == PublishedRecordKind.Vote && votesOnChangedBills.Contains(record.RecordId))
                .Select(record => new PublishedRecordReference(record.Kind, record.RecordId)))
            .Where(record => !skipped.Contains((record.Kind, record.RecordId))).Distinct()];
    }

    private static IEnumerable<PublishedRecordReference> CarriedToRender(ReleaseLinks links, IReadOnlyList<PublishedRecordReference> changedWithoutLinks,
        PublicationBuild build, IReadOnlyList<PendingRecord> additions)
    {
        var skipped = SkippedRecords(build, additions);
        return links.ChangedCitations.Select(cited => new PublishedRecordReference(cited.Kind, cited.RecordId)).Concat(changedWithoutLinks)
            .Where(record => !skipped.Contains((record.Kind, record.RecordId))).Distinct();
    }

    private static HashSet<(PublishedRecordKind Kind, string RecordId)> SkippedRecords(PublicationBuild build, IReadOnlyList<PendingRecord> additions) =>
        [.. additions.Select(addition => (addition.Entry.Kind, addition.RecordId)).Concat(build.Removed.Select(record => (record.Kind, record.RecordId)))];
}
