using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListPublishable(PublishedRecordLoaders loaders, IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<ReadyToPublishList> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var limit = PublishRelease.MaximumRecords;
        var reads = await Task.WhenAll(loaders.All.Select(loader => loader.ListReadyAsync(limit, [], cancellationToken)));
        var ready = reads.Select(read => read.Ready).ToArray();
        var hasMore = reads.Any(read => read.HasMore) || ready.Sum(kind => kind.Count) > limit;
        var withdrawn = await WithdrawnRevisionsAsync(cancellationToken);
        return new([.. PublishApprovedRecords.ShareLimitBetweenKinds(ready, limit).Select(item => item with { WithdrawnRevisionNumber = withdrawn.GetValueOrDefault(item.RecordId) })], hasMore);
    }

    private async Task<Dictionary<string, int?>> WithdrawnRevisionsAsync(CancellationToken cancellationToken)
    {
        var active = (await publications.GetStateAsync(cancellationToken)).Active;
        if (active is null) return [];
        var manifest = await PreviousRelease.LoadManifestAsync(releases, active, cancellationToken);
        return manifest.Withdrawals.ToDictionary(withdrawal => withdrawal.RecordId, withdrawal => (int?)withdrawal.WithdrawnRevisionNumber);
    }
}
