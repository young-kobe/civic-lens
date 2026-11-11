namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The one owner of the tile layout. Each state gets a square grid of equal tiles under its code, in the order of the groups given.
/// A group starts a new row when it does not fit in the width that is left.
/// </summary>
public static class VoteMapTiles
{
    public const double Width = 400;
    public const double Size = 22;
    private const double Gap = 3;
    private const double GroupGap = 16;
    private const double LabelHeight = 17;

    public static VoteMapTileLayout Place(IReadOnlyList<(string State, int Seats)> groups)
    {
        var labels = new List<VoteMapTileLabel>();
        var tiles = new List<VoteMapTile>();
        double x = 0, y = 0, rowHeight = 0;
        foreach (var (state, seats) in groups)
        {
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(seats)));
            var rows = (int)Math.Ceiling(seats / (double)columns);
            var groupWidth = columns * (Size + Gap) - Gap;
            if (x > 0 && x + groupWidth > Width)
            {
                x = 0;
                y += rowHeight + GroupGap;
                rowHeight = 0;
            }
            labels.Add(new(state, x, y + LabelHeight - 4));
            for (var index = 0; index < seats; index++)
                tiles.Add(new(x + index % columns * (Size + Gap), y + LabelHeight + index / columns * (Size + Gap)));
            x += groupWidth + GroupGap;
            rowHeight = Math.Max(rowHeight, rows * (Size + Gap) - Gap + LabelHeight);
        }
        return new(labels, tiles, y + rowHeight + 2);
    }
}
