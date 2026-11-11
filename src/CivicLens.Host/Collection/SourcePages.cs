using System.Globalization;
using CivicLens.Application.Collection.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Core.Votes;

namespace CivicLens.Host.Collection;

internal static class SourcePages
{
    private const string SenateLisUrl = "https://www.senate.gov/legislative/LIS";

    public static string RollCall(Chamber chamber, int congress, int sessionNumber, int rollCallNumber) => chamber == Chamber.Senate
        ? $"{SenateLisUrl}/roll_call_votes/vote{congress}{sessionNumber}/vote_{congress}_{sessionNumber}_{rollCallNumber.ToString("00000", CultureInfo.InvariantCulture)}.htm"
        : $"https://clerk.house.gov/Votes/{CongressCalendar.StartOf(congress).Year + sessionNumber - 1}{rollCallNumber.ToString(CultureInfo.InvariantCulture)}";
}
