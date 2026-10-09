using System.Collections.Immutable;

namespace CivicLens.Application.Paging;

/// <summary>One page in display order (newest first) with cursors toward each neighbour; null means that end.</summary>
public sealed record KeysetSlice<T>(ImmutableArray<T> Items, string? NewerCursor, string? OlderCursor)
{
    /// <param name="fetched">Up to limit + 1 rows in query order: display order for Older and first pages, reverse display order for Newer.</param>
    public static KeysetSlice<T> Create(IReadOnlyList<T> fetched, int limit, PageCursor? cursor,
        Func<T, (long Ticks, string Id)> keyOf)
    {
        var movingNewer = cursor?.Direction == PageDirection.Newer;
        var hasMore = fetched.Count > limit;
        var page = fetched.Take(limit);
        var items = (movingNewer ? page.Reverse() : page).ToImmutableArray();
        var hasNewer = movingNewer ? hasMore : cursor is not null;
        var hasOlder = movingNewer ? cursor is not null : hasMore;
        // An empty page keeps the request's own position so the client can still move back.
        var newest = items.IsEmpty ? (cursor?.Ticks ?? 0, cursor?.Id ?? "") : keyOf(items[0]);
        var oldest = items.IsEmpty ? newest : keyOf(items[^1]);
        return new(items,
            hasNewer ? new PageCursor(PageDirection.Newer, newest.Item1, newest.Item2).Encode() : null,
            hasOlder ? new PageCursor(PageDirection.Older, oldest.Item1, oldest.Item2).Encode() : null);
    }
}
