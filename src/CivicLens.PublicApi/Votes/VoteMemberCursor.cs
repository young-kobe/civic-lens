using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>
/// Where the next page of members starts. It holds the release, so a walk never mixes two releases, and the last position.
/// The vote and its revision come from the route and the active release.
/// </summary>
public sealed record VoteMemberCursor(int Release, int Position)
{
    private const string Tag = "m";

    public static VoteMemberCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 2);
        return new(CursorFields.Release(parts[0]), CursorFields.Number(parts[1]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{Position}");
}
