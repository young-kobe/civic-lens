using System.Globalization;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// The area math of a state outline. The Senate map splits each state at the x that halves its area, so each senator gets equal room.
/// The path holds only move and close commands, so every number pair after M is a point.
/// </summary>
public static class VoteMapArea
{
    private const int Steps = 40;

    public static IReadOnlyList<(double X, double Y)[]> Rings(string path) =>
    [
        .. path.Split('Z', StringSplitOptions.RemoveEmptyEntries).Select(ring =>
        {
            var numbers = ring.Split([' ', 'M'], StringSplitOptions.RemoveEmptyEntries).Select(number => double.Parse(number, CultureInfo.InvariantCulture)).ToArray();
            return Enumerable.Range(0, numbers.Length / 2).Select(index => (numbers[index * 2], numbers[index * 2 + 1])).ToArray();
        })
    ];

    /// <summary>The area of the rings, in square map units.</summary>
    public static double Total(IReadOnlyList<(double X, double Y)[]> rings) => rings.Sum(ring => Math.Abs(Shoelace(ring)));

    /// <summary>The area of the rings on the left of the vertical line at <paramref name="x"/>.</summary>
    public static double LeftOf(IReadOnlyList<(double X, double Y)[]> rings, double x) => rings.Sum(ring => Math.Abs(Shoelace(ClipLeft(ring, x))));

    /// <summary>The x at which half of the area lies on the left, found by bisection between the two edges of the box.</summary>
    public static double HalvingX(IReadOnlyList<(double X, double Y)[]> rings, double left, double right)
    {
        var half = Total(rings) / 2;
        for (var step = 0; step < Steps; step++)
        {
            var middle = (left + right) / 2;
            if (LeftOf(rings, middle) < half) left = middle; else right = middle;
        }
        return (left + right) / 2;
    }

    private static double Shoelace((double X, double Y)[] ring)
    {
        var twice = 0.0;
        for (var index = 0; index < ring.Length; index++)
        {
            var (x1, y1) = ring[index];
            var (x2, y2) = ring[(index + 1) % ring.Length];
            twice += x1 * y2 - x2 * y1;
        }
        return twice / 2;
    }

    /// <summary>Sutherland-Hodgman clip of one ring against the half plane x at or below the line.</summary>
    private static (double X, double Y)[] ClipLeft((double X, double Y)[] ring, double x)
    {
        var clipped = new List<(double X, double Y)>();
        for (var index = 0; index < ring.Length; index++)
        {
            var from = ring[index];
            var to = ring[(index + 1) % ring.Length];
            var fromIn = from.X <= x;
            if (fromIn != (to.X <= x)) clipped.Add((x, from.Y + (to.Y - from.Y) * (x - from.X) / (to.X - from.X)));
            if (to.X <= x) clipped.Add(to);
        }
        return [.. clipped];
    }
}
