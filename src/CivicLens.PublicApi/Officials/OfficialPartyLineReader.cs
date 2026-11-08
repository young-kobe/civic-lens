using System.Data.Common;
using CivicLens.PublicApi.Http;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>Maps the rows of <c>official_party_lines</c> to the public official figure.</summary>
internal static class OfficialPartyLineReader
{
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
