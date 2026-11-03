using CivicLens.Application.Collection;
using CivicLens.Application.Districts;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class DistrictFixtures
{
    public const string SourceId = "census-zcta-districts";
    public const string SourceUrl = "https://www2.census.gov/geo/docs/maps-data/data/rel2020/cd-sld/tab20_cd11920_zcta520_natl.txt";

    // Excerpt of tab20_cd11920_zcta520_natl.txt from the U.S. Census Bureau (public domain, 17 U.S.C. 105), fetched 2026-11-03.
    // The header with its byte order mark and the first two rows are copied from the file. The other rows are edited copies of
    // those rows with other district and ZCTA values, so the Census values of those rows are not real.
    public static readonly string Text = "﻿" + ZctaDistrictParser.Header + "\n" + string.Join('\n',
        "2119035781135773|0101|Congressional District 1|18752973689|2274743296|G5200|N|||||||||558858619|1794806659",
        "2119035781135773|0101|Congressional District 1|18752973689|2274743296|G5200|N|221704258284549|36009|ZCTA5 36009|417245447|1545060|G6350|B5|S|4938464|0",
        Row("0102", "36009", 100),
        Row("0200", "99501", 5000),
        Row("1198", "20001", 6000),
        Row("0614", "94601", 9000),
        Row("01ZZ", "36010", 70),
        Row("0101", "36011", 0)) + "\n";

    public static string Row(string geoid, string zcta, long land) => string.Join('|', "1", geoid, "Congressional District", "1", "2",
        "G5200", "N", "3", zcta, "ZCTA5 " + zcta, "4", "5", "G6350", "B5", "S", land, "0");

    public static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    public static ZctaDistrict[] Rows() => ZctaDistrictParser.Parse(Bytes(Text));

    public static WatchedSourceConfiguration Source() => new()
    {
        Id = SourceId,
        Url = SourceUrl,
        AllowedOrigin = "https://www2.census.gov",
        AllowedPathPrefix = "/geo/docs/maps-data/data/rel2020/cd-sld/tab20_cd11920_zcta520_natl.txt",
        Mode = CollectionMode.Api,
        ApiParser = ApiParser.CensusZctaDistricts,
        CheckIntervalSeconds = 604800,
        MaxRequests = 2,
        MaxBytes = 8_000_000,
        TimeoutSeconds = 60
    };

    public static CollectionConfiguration Configuration() => RosterFixtures.Configuration() with { Sources = [Source()] };
}
