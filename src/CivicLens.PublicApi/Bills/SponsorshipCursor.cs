using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Bills;

/// <summary>Where the next page of sponsorships of one bill starts. The path names the bill, so the cursor holds the release and the last position.</summary>
public sealed record SponsorshipCursor(int Release, int Position)
{
    private const string Tag = "p";

    public static SponsorshipCursor Of(int release, SponsorshipRow last) => new(release, last.Position);

    public static SponsorshipCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 2);
        return new(CursorFields.Release(parts[0]), CursorFields.Number(parts[1]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{Position}");
}
