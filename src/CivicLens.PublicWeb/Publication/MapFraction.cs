using System.Globalization;

namespace CivicLens.PublicWeb.Publication;

/// <summary>A known fraction. An empty base has no percentage and remains distinct from missing evidence.</summary>
public sealed record MapFraction(int Matching, int Base)
{
    public string Text => $"{Matching.ToString("N0", CultureInfo.InvariantCulture)} of {Base.ToString("N0", CultureInfo.InvariantCulture)}";
    public double? Share => Base > 0 && Matching >= 0 && Matching <= Base ? Matching / (double)Base : null;
}
