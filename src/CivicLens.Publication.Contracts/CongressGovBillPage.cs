using System.Globalization;

namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of the Congress.gov address of a bill page, such as "https://www.congress.gov/bill/119th-congress/house-bill/5334".</summary>
public static class CongressGovBillPage
{
    private static readonly Dictionary<string, string> Slugs = new(StringComparer.Ordinal)
    {
        ["hr"] = "house-bill",
        ["s"] = "senate-bill",
        ["hjres"] = "house-joint-resolution",
        ["sjres"] = "senate-joint-resolution",
        ["hconres"] = "house-concurrent-resolution",
        ["sconres"] = "senate-concurrent-resolution",
        ["hres"] = "house-resolution",
        ["sres"] = "senate-resolution"
    };

    /// <summary>The address from the stored bill identity. A bill type outside the table throws.</summary>
    public static string Url(int congress, string billType, int billNumber) =>
        $"https://www.congress.gov/bill/{CongressCalendar.Ordinal(congress)}-congress/{Slugs[billType]}/{billNumber.ToString(CultureInfo.InvariantCulture)}";
}
