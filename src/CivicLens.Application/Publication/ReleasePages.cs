using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Plans every page that this publish writes. A page lists the related records of the new release.</summary>
internal sealed class ReleasePages(IReleaseDirectory releases, PublishedRecordLoaders loaders)
{
    // A page reads and renders when the staging writes it, so one page text is in memory at a time. ReleaseLinks.all still holds the carried relationships.
    public async Task<IReadOnlyDictionary<string, StagedPage>> PlanAsync(PublicationBuild build, IReadOnlyList<PendingRecord> additions,
        bool rewriteAll, CancellationToken cancellationToken)
    {
        var changedWithoutLinks = CarriedWithChangedFacts(build, additions, rewriteAll);
        var links = await ReleaseLinks.LoadAsync(releases, build, loaders.Relationships, additions, changedWithoutLinks, cancellationToken);
        var carriedPages = CarriedToRender(links, changedWithoutLinks, build, additions).ToList();
        var pages = new Dictionary<string, StagedPage>(StringComparer.Ordinal);
        foreach (var addition in additions)
            pages[addition.RecordId] = token => addition.RenderPage(links.Citing(addition.Entry.Kind, addition.RecordId), build.BillLines, token);
        foreach (var carried in carriedPages)
            pages[carried.RecordId] = token => RenderCarriedAsync(carried, links, build, token);
        return pages;
    }

    private async Task<string> RenderCarriedAsync(PublishedRecordReference carried, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await releases.ReadFileAsync(build.Previous.DirectoryName!, PublicationProtocol.RecordDataPath(carried.RecordId),
                PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
            return await loaders.For(carried.Kind).RenderCarriedAsync(carried.RecordId, json, links, build, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            throw new ReleaseRewriteException($"The {carried.Kind} {carried.RecordId} is invalid, so its page cannot be rendered again.", exception);
        }
    }

    // A carried page changes when a link of its related panel changes, when its bill link points at an action that this publish withdraws or returns,
    // when the bill line of that action changed, or when its bill status changed. The links of these pages must load too, so the Related panel stays.
    private static IReadOnlyList<PublishedRecordReference> CarriedWithChangedFacts(PublicationBuild build,
        IReadOnlyList<PendingRecord> additions, bool rewriteAll)
    {
        if (rewriteAll) return AllCarried(build, additions);
        var returnedActions = build.Returned.Where(record => record.Kind == PublishedRecordKind.Action).Select(record => record.RecordId);
        var votesOnChangedBills = build.VotesOnBillsOf(
            build.Removed.Where(record => record.Kind == PublishedRecordKind.Action).Select(record => record.RecordId).Concat(returnedActions)
                .Concat(ActionsWithChangedLine(build, additions)));
        var changedStatuses = ReleaseBillStatuses.ChangedActionIds(
            ReleaseBillStatuses.ActionIdsOf(build.ReleaseRecords), build.Previous, build.BillStatuses);
        var skipped = SkippedRecords(build, additions);
        return [.. changedStatuses.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))
            .Concat(build.ReleaseRecords.Where(record => record.Kind == PublishedRecordKind.Vote && votesOnChangedBills.Contains(record.RecordId))
                .Select(record => new PublishedRecordReference(record.Kind, record.RecordId)))
            .Where(record => !skipped.Contains((record.Kind, record.RecordId))).Distinct()];
    }

    private static IEnumerable<string> ActionsWithChangedLine(PublicationBuild build, IReadOnlyList<PendingRecord> additions) =>
        additions.Where(addition => addition.Entry.Kind == PublishedRecordKind.Action
                && build.Previous.Find(PublishedRecordKind.Action, addition.RecordId)?.BillLine != addition.Entry.BillLine)
            .Select(addition => addition.RecordId);

    private static IReadOnlyList<PublishedRecordReference> AllCarried(PublicationBuild build, IReadOnlyList<PendingRecord> additions)
    {
        var skipped = SkippedRecords(build, additions);
        return [.. build.ReleaseRecords.Select(record => new PublishedRecordReference(record.Kind, record.RecordId))
            .Where(record => !skipped.Contains((record.Kind, record.RecordId)))];
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
