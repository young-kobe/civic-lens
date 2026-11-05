using CivicLens.PublicApi.Http;
using Microsoft.AspNetCore.Http;

namespace CivicLens.PublicApi.ZipDistricts;

public sealed record ZipDistrictRequest
{
    private const int ZctaLength = 5;

    public required string Zcta { get; init; }
    /// <summary>Null selects the newest Congress that the ZCTA has rows for.</summary>
    public int? Congress { get; init; }

    /// <summary>Reads the path value and the query string. An invalid value throws <see cref="ArgumentException"/> that names the parameter.</summary>
    public static ZipDistrictRequest Parse(string zcta, IQueryCollection query) => new()
    {
        Zcta = zcta.Length == ZctaLength && zcta.All(char.IsAsciiDigit) ? zcta : throw new ArgumentException($"zcta must have {ZctaLength} digits.", nameof(zcta)),
        Congress = QueryValues.Number(query, "congress", 1, int.MaxValue)
    };
}
