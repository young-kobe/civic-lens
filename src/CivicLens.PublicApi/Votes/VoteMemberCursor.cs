using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Votes;

/// <summary>
/// Where the next page of members starts. It holds the release, so a walk never mixes two releases, and the last position.
/// The vote and its revision come from the route and the active release.
/// </summary>
public sealed record VoteMemberCursor(int Release, int Position)
{
    private const int MaximumLength = 40;
    private const string Tag = "m";

    public static VoteMemberCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = value.Split('.');
        if (value.Length > MaximumLength || parts is not [Tag, var release, var position] ||
            !int.TryParse(release, NumberStyles.None, CultureInfo.InvariantCulture, out var releaseNumber) || releaseNumber < 1 ||
            !int.TryParse(position, NumberStyles.None, CultureInfo.InvariantCulture, out var positionNumber))
            throw QueryValues.InvalidCursor();
        return new(releaseNumber, positionNumber);
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{Position}");
}
