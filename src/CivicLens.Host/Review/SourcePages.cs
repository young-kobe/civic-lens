using System.Globalization;
using CivicLens.Core.Relationships;
using CivicLens.Core.Votes;

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

    public static string RollCall(Chamber chamber, int congress, int sessionNumber, int rollCallNumber) => chamber == Chamber.Senate
        ? $"https://www.senate.gov/legislative/LIS/roll_call_votes/vote{congress}{sessionNumber}/vote_{congress}_{sessionNumber}_{rollCallNumber:D5}.htm"
        : $"https://clerk.house.gov/Votes/{CongressCalendar.StartOf(congress).Year + sessionNumber - 1}{rollCallNumber.ToString(CultureInfo.InvariantCulture)}";

    private static string Ordinal(int number)
    {
        var suffix = number % 100 is >= 11 and <= 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return number.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
