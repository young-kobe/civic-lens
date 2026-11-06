using System.Globalization;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

/// <summary>One roll call as the owner measure names it, for example house-119-1-100 or senate-119-2-45. Only the exact form that <see cref="Id"/> writes is valid.</summary>
public sealed record VoteMeasureRoll(Chamber Chamber, int Congress, int SessionNumber, int RollCallNumber)
{
    public string Id => string.Create(CultureInfo.InvariantCulture, $"{Chamber.ToString().ToLowerInvariant()}-{Congress}-{SessionNumber}-{RollCallNumber}");

    public static VoteMeasureRoll? TryParse(string id)
    {
        if (id.Split('-') is not [var chamber, var congress, var session, var roll] || !Enum.TryParse<Chamber>(chamber, ignoreCase: true, out var parsed) ||
            !int.TryParse(congress, NumberStyles.None, CultureInfo.InvariantCulture, out var congressNumber) ||
            !int.TryParse(session, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionNumber) ||
            !int.TryParse(roll, NumberStyles.None, CultureInfo.InvariantCulture, out var rollNumber)) return null;
        var parsedRoll = new VoteMeasureRoll(parsed, congressNumber, sessionNumber, rollNumber);
        return parsedRoll.Id == id && congressNumber > 0 && sessionNumber is 1 or 2 && rollNumber > 0 ? parsedRoll : null;
    }
}
