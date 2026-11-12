namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of the printed form of a bill number, such as "H.R. 9576", "H.J.Res. 213" or "S. 2403", and of the chamber that a bill type starts in.
/// Stored code text and every public page read this table.</summary>
public static class BillNumbers
{
    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.Ordinal)
    {
        ["hr"] = "H.R.",
        ["s"] = "S.",
        ["hjres"] = "H.J.Res.",
        ["sjres"] = "S.J.Res.",
        ["hconres"] = "H.Con.Res.",
        ["sconres"] = "S.Con.Res.",
        ["hres"] = "H.Res.",
        ["sres"] = "S.Res."
    };

    /// <summary>The printed bill number for display. A type outside the table, which a published vote can hold, shows in upper case.</summary>
    public static string Of(string billType, int number) =>
        $"{(TypeLabels.TryGetValue(billType.ToLowerInvariant(), out var label) ? label : billType.ToUpperInvariant())} {number}";

    /// <summary>The printed label of a bill type for stored, code approved text. A type outside the table throws, so no unreviewed label reaches a stored headline.</summary>
    public static string TypeLabel(string billType) => TypeLabels[billType.ToLowerInvariant()];

    /// <summary>The chamber where a bill of this type is introduced. Every House type starts with "h", and every Senate type starts with "s".</summary>
    public static PublishedChamber ChamberOf(string billType) =>
        billType.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? PublishedChamber.House : PublishedChamber.Senate;
}
