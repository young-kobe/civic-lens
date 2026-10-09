using CivicLens.Application.Paging;

namespace CivicLens.Tests.Fixtures;

/// <summary>Walks a keyset-paged list to its oldest page with Older cursors, then back to the newest with Newer cursors.</summary>
internal sealed record PageWalk(List<string[]> Down, List<string[]> Up, string? FirstNewer, string? LastOlder)
{
    public static async Task<PageWalk> RunAsync(Func<PageCursor?, Task<(string[] Ids, string? Newer, string? Older)>> read)
    {
        var down = new List<string[]>();
        PageCursor? cursor = null;
        (string[] Ids, string? Newer, string? Older) page;
        string? firstNewer = null;
        do
        {
            page = await read(cursor);
            if (down.Count == 0) firstNewer = page.Newer;
            down.Add(page.Ids);
            cursor = PageCursor.Parse(page.Older);
            Assert.True(down.Count <= 20, "Older paging did not end.");
        } while (cursor is not null);
        var up = new List<string[]>();
        var back = PageCursor.Parse(page.Newer);
        while (back is not null)
        {
            var previous = await read(back);
            up.Add(previous.Ids);
            back = PageCursor.Parse(previous.Newer);
            Assert.True(up.Count <= 20, "Newer paging did not end.");
        }
        return new(down, up, firstNewer, page.Older);
    }

    public void AssertExact(string[] expected)
    {
        Assert.Equal(expected, Down.SelectMany(ids => ids));
        Assert.Null(FirstNewer);
        Assert.Null(LastOlder);
        Assert.Equal(Down.Count - 1, Up.Count);
        // Paging back must show the very same pages in reverse, so no row is skipped or repeated at a page edge.
        Assert.Equal(Down.SkipLast(1).Reverse().Select(ids => string.Join(',', ids)), Up.Select(ids => string.Join(',', ids)));
    }
}
