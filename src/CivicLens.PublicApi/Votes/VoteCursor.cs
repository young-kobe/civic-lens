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
    private const string Tag = "v";

    public static VoteCursor Of(int release, VoteRow last) =>
        new(release, last.VotedOn, last.Chamber, last.Session, last.RollCall, last.RecordId);

    public static VoteCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 6);
        var chamber = (PublishedChamber)CursorFields.Number(parts[2]);
        return Enum.IsDefined(chamber)
            ? new(CursorFields.Release(parts[0]), CursorFields.Date(parts[1]), chamber, CursorFields.Number(parts[3]), CursorFields.Number(parts[4]), CursorFields.RecordId(parts[5]))
            : throw QueryValues.InvalidCursor();
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture,
        $"{Tag}.{Release}.{CursorFields.Format(VotedOn)}.{(int)Chamber}.{Session}.{RollCall}.{RecordId}");
}
