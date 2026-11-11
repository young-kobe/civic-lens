namespace CivicLens.PublicWeb.Components.Evidence;

public sealed record CitedRange(string AnchorId, int Start, int Length);

public static class CitedText
{
    /// <summary>The anchor ID of the citation at this position. The cited text marks the same ID that the quote link points to.</summary>
    public static string Anchor(int index) => $"cite-{index + 1}";

    public static IReadOnlyList<TextRun> Split(string text, IReadOnlyList<CitedRange> ranges)
    {
        var edges = ranges.SelectMany(range => new[] { range.Start, range.Start + range.Length })
            .Append(0).Append(text.Length).Distinct().Order().ToArray();
        var runs = new List<TextRun>();
        for (var i = 0; i + 1 < edges.Length; i++)
        {
            var start = edges[i];
            var end = edges[i + 1];
            var marks = ranges.Where(range => range.Start <= start && range.Start + range.Length >= end)
                .Select(range => new RunMark(range.AnchorId, range.Start == start)).ToArray();
            runs.Add(new TextRun(text[start..end], marks));
        }
        return runs;
    }
}
