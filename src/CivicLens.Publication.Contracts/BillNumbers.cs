namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of the printed form of a bill number, such as "H.R. 9576", "H.J.Res. 213" or "S. 2403", and of the chamber that a bill type starts in.
/// Stored code text and every public page read this table.</summary>
public static class BillNumbers
{
    private static readonly Dictionary<string, (string Label, PublishedChamber Chamber)> Types = new(StringComparer.Ordinal)
    {
        ["hr"] = ("H.R.", PublishedChamber.House),
        ["s"] = ("S.", PublishedChamber.Senate),
        ["hjres"] = ("H.J.Res.", PublishedChamber.House),
        ["sjres"] = ("S.J.Res.", PublishedChamber.Senate),
        ["hconres"] = ("H.Con.Res.", PublishedChamber.House),
        ["sconres"] = ("S.Con.Res.", PublishedChamber.Senate),
        ["hres"] = ("H.Res.", PublishedChamber.House),
        ["sres"] = ("S.Res.", PublishedChamber.Senate)
    };

    /// <summary>The printed bill number for display. A type outside the table, which a published vote can hold, shows in upper case.</summary>
    public static string Of(string billType, int number) =>
        $"{(Types.TryGetValue(billType.ToLowerInvariant(), out var type) ? type.Label : billType.ToUpperInvariant())} {number}";

    /// <summary>The printed label of a bill type for stored, code approved text. A type outside the table throws, so no unreviewed label reaches a stored headline.</summary>
    public static string TypeLabel(string billType) => Types[billType.ToLowerInvariant()].Label;

    /// <summary>The chamber where a bill of this type is introduced. A type outside the table throws, so no unknown type reads as a chamber.</summary>
    public static PublishedChamber ChamberOf(string billType) => Types[billType.ToLowerInvariant()].Chamber;
}
