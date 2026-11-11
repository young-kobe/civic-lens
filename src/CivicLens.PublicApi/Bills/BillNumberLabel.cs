namespace CivicLens.PublicApi.Bills;

/// <summary>
/// The display style of a bill number, such as "H.R. 5334". It repeats the table of <c>ActionCodeText.BillNumber</c>, which PublicApi cannot reference.
/// <c>BillNumberLabelTests</c> compares both tables.
/// </summary>
public static class BillNumberLabel
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
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

    public static string Of(string billType, int number) =>
        $"{(Labels.TryGetValue(billType.ToLowerInvariant(), out var label) ? label : billType.ToUpperInvariant())} {number}";
}
