using System.Globalization;
using CivicLens.Core.Relationships;

namespace CivicLens.Host.Review;

internal static class SourcePages
{
    private static readonly Dictionary<string, string> BillSlugs = new(StringComparer.Ordinal)
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

    public static string Bill(int congress, string billType, int billNumber) =>
        $"https://www.congress.gov/bill/{Ordinal(congress)}-congress/{BillSlugs[billType]}/{billNumber.ToString(CultureInfo.InvariantCulture)}";

    public static string RollCall(int congress, int sessionNumber, int rollCallNumber) =>
        $"https://clerk.house.gov/evs/{CongressCalendar.StartOf(congress).Year + sessionNumber - 1}/roll{rollCallNumber.ToString("000", CultureInfo.InvariantCulture)}.xml";

    private static string Ordinal(int number)
    {
        var suffix = number % 100 is >= 11 and <= 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
