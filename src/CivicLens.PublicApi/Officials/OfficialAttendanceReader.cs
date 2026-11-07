using System.Data.Common;
using CivicLens.PublicApi.Http;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>Reads the latest attendance Congress and its figures for each official.</summary>
internal static class OfficialAttendanceReader
{
    public const string LatestSql = """
        SELECT official_id, chamber, congress, held, missed, first_voted_on, last_voted_on
          FROM (
              SELECT official_id, chamber, congress, held, missed, first_voted_on, last_voted_on,
                     dense_rank() OVER (PARTITION BY official_id ORDER BY congress DESC) AS newest
                FROM public_api.official_attendance
               WHERE official_id = ANY(@attendanceIds)
          ) ranked
         WHERE newest = 1
        """;

    public static MissedVotesFigure ReadFigure(DbDataReader reader, int first)
    {
        var chamber = ViewColumns.Enum<PublishedChamber>(reader, first);
        var congress = reader.GetInt32(first + 1);
        return new(chamber, congress, ViewColumns.Number(reader, first + 3)!.Value, ViewColumns.Number(reader, first + 2)!.Value,
            reader.GetFieldValue<DateOnly>(first + 4), reader.GetFieldValue<DateOnly>(first + 5), OfficialScopes.MissedVotes(chamber, congress));
    }
}
