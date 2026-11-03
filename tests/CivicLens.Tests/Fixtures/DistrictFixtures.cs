using CivicLens.Application.Collection;
using CivicLens.Application.Districts;
using CivicLens.Collection.Contracts;

namespace CivicLens.Tests.Fixtures;

internal static class DistrictFixtures
{
    public const string SourceId = "census-zcta-districts";
    public const string SourceUrl = "https://www2.census.gov/geo/docs/maps-data/data/rel2020/cd-sld/tab20_cd11920_zcta520_natl.txt";

    // Rows of tab20_cd11920_zcta520_natl.txt from the U.S. Census Bureau (public domain, 17 U.S.C. 105).
    // Source: https://www2.census.gov/geo/docs/maps-data/data/rel2020/cd-sld/tab20_cd11920_zcta520_natl.txt, fetched 2026-11-03.
    // Rows are copied unchanged, except the last row: it is a 09ZZ row with its part land area edited from 0 to 1.
    public static readonly string Text = "\uFEFF" + ZctaDistrictParser.Header + "\n" + string.Join('\n',
        "2119035781135773|0101|Congressional District 1|18752973689|2274743296|G5200|N|||||||||558858619|1794806659",
        "2119035781135773|0101|Congressional District 1|18752973689|2274743296|G5200|N|221704258284549|36009|ZCTA5 36009|417245447|1545060|G6350|B5|S|4938464|0",
        "2119035781135774|0102|Congressional District 2|24514185142|234887544|G5200|N|221704258284549|36009|ZCTA5 36009|417245447|1545060|G6350|B5|S|412306983|1545060",
        "2119035963374560|0200|Congressional District (at Large)|1478941109938|245380162784|G5200|N|221704258437715|99501|ZCTA5 99501|17227675|468698|G6350|B5|S|17227675|468698",
        "2119035963374576|1198|Delegate District (at Large)|158316124|18709762|G5200|N|221704257714557|20001|ZCTA5 20001|5302364|159887|G6350|B5|S|5302364|159887",
        "2119035951820630|0614|Congressional District 14|1503352878|74948436|G5200|N|221704258476682|94505|ZCTA5 94505|43179650|15006170|G6350|B5|S|1679865|57311",
        "2119035781135774|0102|Congressional District 2|24514185142|234887544|G5200|N|221704258287104|36571|ZCTA5 36571|101709975|925646|G6350|B5|S|0|42570",
        "2119035951820684|09ZZ|Congressional Districts not defined|0|1095660734|G5200|F|221704257530392|06437|ZCTA5 06437|122036796|4075183|G6350|B5|S|1|1452875") + "\n";

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
