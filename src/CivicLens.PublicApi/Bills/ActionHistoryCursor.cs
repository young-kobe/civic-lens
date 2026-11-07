using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Bills;

public sealed record ActionHistoryCursor(int Release, int Position)
{
    private const string Tag = "ba";

    public static ActionHistoryCursor Of(int release, BillActionRow last) => new(release, last.Position);

    public static ActionHistoryCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 2);
        return new(CursorFields.Release(parts[0]), CursorFields.Number(parts[1]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{Position}");
}
