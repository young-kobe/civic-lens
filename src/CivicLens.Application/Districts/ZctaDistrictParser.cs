using System.Globalization;
using System.Text;

namespace CivicLens.Application.Districts;

public static class ZctaDistrictParser
{
    public const string Header =
        "OID_CD119_20|GEOID_CD119_20|NAMELSAD_CD119_20|AREALAND_CD119_20|AREAWATER_CD119_20|MTFCC_CD119_20|FUNCSTAT_CD119_20|" +
        "OID_ZCTA5_20|GEOID_ZCTA5_20|NAMELSAD_ZCTA5_20|AREALAND_ZCTA5_20|AREAWATER_ZCTA5_20|MTFCC_ZCTA5_20|CLASSFP_ZCTA5_20|" +
        "FUNCSTAT_ZCTA5_20|AREALAND_PART|AREAWATER_PART";

    private const int ColumnCount = 17;
    private const int DistrictColumn = 1;
    private const int ZctaColumn = 8;
    private const int LandPartColumn = 15;
    private const string NoDistrict = "ZZ";
    private const string AtLarge = "00";
    private const string Delegate = "98";

    public static ZctaDistrict[] Parse(ReadOnlySpan<byte> file)
    {
        var lines = Encoding.UTF8.GetString(file).TrimStart('﻿').Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (lines[0] != Header)
            throw new InvalidDataException("ZCTA district file has an unexpected header.");
        var rows = lines.Skip(1).Where(line => line.Length > 0).Select(ReadRow).OfType<ZctaDistrict>().ToArray();
        if (rows.Length == 0)
            throw new InvalidDataException("ZCTA district file must list at least one ZCTA.");
        if (rows.DistinctBy(row => (row.Zcta, row.State, row.District)).Count() != rows.Length)
            throw new InvalidDataException("ZCTA district file must list each ZCTA and district once.");
        return rows;
    }

    private static ZctaDistrict? ReadRow(string line)
    {
        var columns = line.Split('|');
        if (columns.Length != ColumnCount)
            throw new InvalidDataException("ZCTA district file has a row with the wrong number of columns.");
        var geoid = columns[DistrictColumn];
        var zcta = columns[ZctaColumn];
        if (geoid.Length != 4 || !long.TryParse(columns[LandPartColumn], NumberStyles.None, CultureInfo.InvariantCulture, out var land) ||
            zcta.Length is not (0 or 5) || !zcta.All(char.IsAsciiDigit))
            throw new InvalidDataException("ZCTA district file has a malformed row.");
        var district = geoid[2..];
        if (zcta.Length == 0 || land == 0 || district == NoDistrict) return null;
        return new ZctaDistrict(zcta, StateFips.CodeOf(geoid[..2]), DistrictNumberOf(district), land);
    }

    private static int DistrictNumberOf(string district)
    {
        if (district is AtLarge or Delegate) return 0;
        return district.All(char.IsAsciiDigit) ? int.Parse(district, CultureInfo.InvariantCulture)
            : throw new InvalidDataException("ZCTA district file has a malformed district.");
    }
}
