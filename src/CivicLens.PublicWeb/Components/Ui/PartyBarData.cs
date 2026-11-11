using System.Globalization;

namespace CivicLens.PublicWeb.Components.Ui;

/// <summary>The labeled segments of one party bar. A segment with no members is listed in the legend and not drawn.</summary>
public sealed record PartyBarData(string Name, IReadOnlyList<PartyBarSegment> Segments, PartyBarMark? Mark = null)
{
    public int Total => Segments.Sum(segment => segment.Count);

    public string Description => $"{Name}: " + string.Join(", ", Segments.Select(segment => $"{segment.Label} {segment.Count}"));

    public static string Number(double count) => count.ToString("0.#", CultureInfo.InvariantCulture);
}
