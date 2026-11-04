using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>
/// Where the next page of votes starts. It holds the release, so a walk never mixes two releases, and the whole sort key, so the query needs no lookup.
/// A client can forge any key. A forged key only moves the window over rows that are public anyway.
/// </summary>
public sealed record VoteCursor(int Release, DateOnly VotedOn, PublishedChamber Chamber, int Session, int RollCall, string RecordId)
{
    private const int MaximumLength = 200;
    private const string DateFormat = "yyyyMMdd";
    private const string Tag = "v";

    public static VoteCursor Of(int release, VoteRow last) =>
        new(release, last.VotedOn, last.Chamber, last.Session, last.RollCall, last.RecordId);

    public static VoteCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split('.');
        if (value.Length > MaximumLength || parts is not [Tag, var release, var date, var chamber, var session, var rollCall, var id]) throw QueryValues.InvalidCursor();
        if (!int.TryParse(release, NumberStyles.None, CultureInfo.InvariantCulture, out var releaseNumber) || releaseNumber < 1 ||
            !DateOnly.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var votedOn) ||
            !int.TryParse(chamber, NumberStyles.None, CultureInfo.InvariantCulture, out var chamberNumber) || !Enum.IsDefined((PublishedChamber)chamberNumber) ||
            !int.TryParse(session, NumberStyles.None, CultureInfo.InvariantCulture, out var sessionNumber) ||
            !int.TryParse(rollCall, NumberStyles.None, CultureInfo.InvariantCulture, out var rollCallNumber) ||
            !PublicationProtocol.IsAnyRecordId(id))
            throw QueryValues.InvalidCursor();
        return new(releaseNumber, votedOn, (PublishedChamber)chamberNumber, sessionNumber, rollCallNumber, id);
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture,
        $"{Tag}.{Release}.{VotedOn.ToString(DateFormat, CultureInfo.InvariantCulture)}.{(int)Chamber}.{Session}.{RollCall}.{RecordId}");
}
