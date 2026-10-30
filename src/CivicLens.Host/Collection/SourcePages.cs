using System.Globalization;
using CivicLens.Application.Collection.Votes;
using CivicLens.Core.Relationships;
using CivicLens.Core.Votes;

namespace CivicLens.Host.Collection;

internal static class SourcePages
{
    private const string SenateLisUrl = "https://www.senate.gov/legislative/LIS";

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
        $"https://www.congress.gov/bill/{CongressCalendar.Ordinal(congress)}-congress/{BillSlugs[billType]}/{billNumber.ToString(CultureInfo.InvariantCulture)}";

    public static string RollCall(Chamber chamber, int congress, int sessionNumber, int rollCallNumber) => chamber == Chamber.Senate
        ? $"{SenateLisUrl}/roll_call_votes/vote{congress}{sessionNumber}/vote_{congress}_{sessionNumber}_{rollCallNumber.ToString("00000", CultureInfo.InvariantCulture)}.htm"
        : $"https://clerk.house.gov/Votes/{CongressCalendar.StartOf(congress).Year + sessionNumber - 1}{rollCallNumber.ToString(CultureInfo.InvariantCulture)}";
}
