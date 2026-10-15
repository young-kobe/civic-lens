using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListPublishable(PublishedRecordLoaders loaders)
{
    public async Task<ReadyToPublishList> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var limit = PublishRelease.MaximumRecords;
        var reads = await Task.WhenAll(loaders.All.Select(loader => loader.ListReadyAsync(limit, cancellationToken)));
        var ready = reads.Select(read => read.Ready).ToArray();
        var hasMore = reads.Any(read => read.HasMore) || ready.Sum(kind => kind.Count) > limit;
        return new(ShareLimitBetweenKinds(ready, limit), hasMore);
    }

    // Each kind gets an equal share of the limit. A kind with fewer ready records gives its unused share to the others.
    private static ImmutableArray<ReadyToPublishRecord> ShareLimitBetweenKinds(IReadOnlyList<ReadyToPublishRecord>[] kinds, int limit)
    {
        var counts = new int[kinds.Length];
        var remaining = limit;
        var smallestFirst = Enumerable.Range(0, kinds.Length).OrderBy(kind => kinds[kind].Count).ToArray();
        for (var position = 0; position < smallestFirst.Length; position++)
        {
            var kind = smallestFirst[position];
            counts[kind] = Math.Min(kinds[kind].Count, remaining / (smallestFirst.Length - position));
            remaining -= counts[kind];
        }
        return [.. kinds.SelectMany((ready, kind) => ready.Take(counts[kind]))];
    }
}
