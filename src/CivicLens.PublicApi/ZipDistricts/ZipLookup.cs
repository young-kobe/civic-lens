namespace CivicLens.PublicApi.ZipDistricts;

/// <summary>The outcome of a lookup. Exactly one of the answer or the reason is set.</summary>
public sealed record ZipLookup(ZipDistrictAnswer? Answer, ZipLookupMiss? Miss)
{
    public static ZipLookup NoRelease { get; } = new(null, ZipLookupMiss.NoRelease);
    public static ZipLookup NoNames { get; } = new(null, ZipLookupMiss.NoNames);
    public static ZipLookup NotFound { get; } = new(null, ZipLookupMiss.NotFound);

    public static ZipLookup Of(ZipDistrictAnswer answer) => new(answer, null);
}

public enum ZipLookupMiss { NoRelease, NoNames, NotFound }
