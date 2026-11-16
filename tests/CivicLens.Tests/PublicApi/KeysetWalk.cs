namespace CivicLens.Tests.PublicApi;

/// <summary>
/// Walks a keyset list both ways. It follows every next cursor to the last page, then every previous cursor back to the first page.
/// Each previous page must give the rows and the cursors of the page that the forward walk read before it, and only the first page has no previous cursor (#580 item 7).
/// </summary>
internal static class KeysetWalk
{
    private const int MaximumPages = 50;

    public sealed record Page(string[] Ids, string? Next, string? Previous);

    /// <summary>The read of one page: the cursor or null, and true when the cursor starts a previous page. It returns the IDs of the rows in list order.</summary>
    public static async Task<string[]> BothWaysAsync(Func<string?, bool, Task<Page>> read)
    {
        var page = await read(null, false);
        Assert.Null(page.Previous);
        List<Page> forward = [page];
        while (page.Next is { } next && forward.Count < MaximumPages)
        {
            page = await read(next, false);
            Assert.NotNull(page.Previous);
            forward.Add(page);
        }
        Assert.Null(page.Next);
        for (var index = forward.Count - 1; index > 0; index--)
        {
            var back = await read(forward[index].Previous, true);
            Assert.Equal(forward[index - 1].Ids, back.Ids);
            Assert.Equal(forward[index - 1].Next, back.Next);
            Assert.Equal(forward[index - 1].Previous, back.Previous);
        }
        return [.. forward.SelectMany(seen => seen.Ids)];
    }
}
