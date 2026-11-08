using System.Data.Common;
using CivicLens.PublicApi.Http;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>Maps the rows of <c>official_party_lines</c> to the public official figure.</summary>
internal static class OfficialPartyLineReader
{
    public const string ForAttendanceSql = """
        SELECT lines.official_id, lines.chamber, lines.congress, lines.party, lines.with_party, lines.against_party,
               lines.cast_count, lines.first_voted_on, lines.last_voted_on
          FROM unnest(@officialIds, @congresses) AS latest(official_id, congress)
          JOIN public_api.official_party_lines lines
            ON lines.official_id = latest.official_id AND lines.congress = latest.congress
         WHERE lines.official_id = ANY(@officialIds)
         ORDER BY lines.official_id, lines.chamber, lines.party
        """;

    public static PartyLineFigure ReadFigure(DbDataReader reader, int first)
    {
        var chamber = ViewColumns.Enum<PublishedChamber>(reader, first);
        var congress = reader.GetInt32(first + 1);
        var party = reader.GetString(first + 2);
        return new(chamber, congress, party, ViewColumns.Number(reader, first + 3)!.Value, ViewColumns.Number(reader, first + 4)!.Value,
            ViewColumns.Number(reader, first + 5)!.Value, reader.GetFieldValue<DateOnly>(first + 6), reader.GetFieldValue<DateOnly>(first + 7),
            OfficialScopes.PartyLine(chamber, congress, party));
    }
}
