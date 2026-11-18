using System.Globalization;

namespace CivicLens.Publication.Contracts;

/// <summary>The official House Clerk or Senate page for a roll call.</summary>
public static class OfficialRollCallPage
{
    /// <summary>The address from the stored chamber, Congress, session, and roll call number.</summary>
    public static string Url(PublishedChamber chamber, int congress, int sessionNumber, int rollCallNumber) => chamber switch
    {
        PublishedChamber.House => string.Create(CultureInfo.InvariantCulture,
            $"https://clerk.house.gov/Votes/{CongressCalendar.StartOf(congress).Year + sessionNumber - 1}{rollCallNumber}"),
        PublishedChamber.Senate => string.Create(CultureInfo.InvariantCulture,
            $"https://www.senate.gov/legislative/LIS/roll_call_votes/vote{congress}{sessionNumber}/vote_{congress}_{sessionNumber}_{rollCallNumber:00000}.htm"),
        _ => throw new ArgumentOutOfRangeException(nameof(chamber), chamber, "Chamber has no official roll call page.")
    };
}
