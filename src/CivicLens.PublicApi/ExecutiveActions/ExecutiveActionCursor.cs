using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Where the next page of executive actions starts. It holds the release and the whole sort key.</summary>
public sealed record ExecutiveActionCursor(int Release, DateOnly SignedOn, string RecordId)
{
    private const string Tag = "x";

    public static ExecutiveActionCursor Of(int release, ExecutiveActionRow last) => new(release, last.SignedOn, last.RecordId);

    public static ExecutiveActionCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{CursorFields.Format(SignedOn)}.{RecordId}");
}
