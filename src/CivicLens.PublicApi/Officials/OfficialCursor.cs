using System.Buffers.Text;
using System.Globalization;
using System.Text;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// Where the next page of officials starts. It holds the release, so a walk never mixes two releases, and the whole sort key, so the query needs no lookup.
/// The key is encoded as one field, because a name holds dots. A client can forge any key. A forged key only moves the window over rows that are public anyway.
/// </summary>
public sealed record OfficialCursor(int Release, string Name, string OfficialId)
{
    private const string Tag = "o";
    private const char Separator = '\u001f';

    public static OfficialCursor Of(int release, OfficialRow last) => new(release, last.Name, last.OfficialId);

    public static OfficialCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 2);
        var key = Decode(parts[1]).Split(Separator);
        return key.Length == 2 && key[0].Length > 0 && key[1].Length > 0 ? new(CursorFields.Release(parts[0]), key[0], key[1]) : throw QueryValues.InvalidCursor();
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture,
        $"{Tag}.{Release}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Name + Separator + OfficialId))}");

    private static string Decode(string field)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(field));
        }
        catch (FormatException)
        {
            throw QueryValues.InvalidCursor();
        }
    }
}
