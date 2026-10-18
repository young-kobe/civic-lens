using System.Collections.Immutable;

namespace CivicLens.Application.Paging;

public sealed record KeysetSlice<T>(ImmutableArray<T> Items, string? NewerCursor, string? OlderCursor)
{
    public static KeysetSlice<T> Create(IReadOnlyList<T> fetched, int limit, PageCursor? cursor,
        Func<T, (long Ticks, string Id)> keyOf) =>
        Create(fetched, limit, cursor, row =>
        {
            var (ticks, id) = keyOf(row);
            return ((int?)null, ticks, id);
        });

    public static KeysetSlice<T> Create(IReadOnlyList<T> fetched, int limit, PageCursor? cursor,
        Func<T, (int? Rank, long Ticks, string Id)> keyOf)
    {
        var movingNewer = cursor?.Direction == PageDirection.Newer;
        var hasMore = fetched.Count > limit;
        var page = fetched.Take(limit);
        var items = (movingNewer ? page.Reverse() : page).ToImmutableArray();
        var hasNewer = movingNewer ? hasMore : cursor is not null;
        var hasOlder = movingNewer ? cursor is not null : hasMore;
        (int? Rank, long Ticks, string Id) newest = items.IsEmpty ? (cursor?.Rank, cursor?.Ticks ?? 0, cursor?.Id ?? "") : keyOf(items[0]);
        var oldest = items.IsEmpty ? newest : keyOf(items[^1]);
        return new(items,
            hasNewer ? new PageCursor(PageDirection.Newer, newest.Ticks, newest.Id, newest.Rank).Encode() : null,
            hasOlder ? new PageCursor(PageDirection.Older, oldest.Ticks, oldest.Id, oldest.Rank).Encode() : null);
    }
}
