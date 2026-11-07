using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Votes;
using Npgsql;

namespace CivicLens.PublicApi.Home;

/// <summary>Reads votes, bills, and briefs through one statement from one active release.</summary>
public sealed class HomeStore(NpgsqlDataSource views)
{
    private const int VotesPerChamber = 2;

    private const string Sql = $"""
        SELECT a.release_number, b.record_id, b.week_start, b.headline, b.label,
               s.is_featured, s.record_id, s.voted_on, s.chamber, s.congress, s.session_number, s.roll_call_number,
               s.headline, s.label, s.result, s.yea, s.nay, s.bill_type, s.bill_number, s.bill_line, s.summary, s.text_origin
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT v.record_id FROM public_api.votes v
                WHERE v.kind = 'Passage' AND v.bill_type IS NOT NULL AND v.bill_number IS NOT NULL
                ORDER BY {VoteStore.Order} LIMIT 1
          ) featured_key ON TRUE
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.votes v
                WHERE v.record_id = featured_key.record_id LIMIT 1
          ) featured ON TRUE
          LEFT JOIN LATERAL (SELECT * FROM public_api.briefs b ORDER BY b.week_start DESC, b.record_id LIMIT 1) b ON TRUE
          LEFT JOIN LATERAL (
               SELECT v.*, (SELECT l.bill_line FROM public_api.bills l
                             WHERE l.congress = v.congress AND l.bill_type = v.bill_type AND l.bill_number = v.bill_number
                             ORDER BY l.record_id LIMIT 1) AS bill_line
                 FROM LATERAL (
                      SELECT true AS is_featured, featured.* WHERE featured.record_id IS NOT NULL
                      UNION ALL
                      SELECT false AS is_featured, recent.*
                        FROM unnest(@chambers) AS c(chamber)
                       CROSS JOIN LATERAL (
                            SELECT v.* FROM public_api.votes v
                             WHERE v.chamber = c.chamber AND v.kind = ANY(@kinds) AND v.yea IS NOT NULL
                               AND (featured.record_id IS NULL OR v.record_id <> featured.record_id)
                             ORDER BY {VoteStore.Order} LIMIT @take) recent
                 ) v
          ) s ON TRUE
         ORDER BY s.is_featured DESC NULLS LAST, s.voted_on DESC, s.chamber, s.session_number DESC, s.roll_call_number DESC, s.record_id
        """;

    /// <summary>Reads the home answer. Null means there is no active release.</summary>
    public async Task<HomeAnswer?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("chambers", Enum.GetNames<PublishedChamber>());
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        command.Parameters.AddWithValue("take", VotesPerChamber);
        var votes = new List<HomeVote>();
        HomeFeaturedVote? featured = null;
        HomeBrief? brief = null;
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            brief ??= reader.IsDBNull(1) ? null : new(reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), ViewColumns.Text(reader, 4));
            if (reader.IsDBNull(5)) continue;
            if (reader.GetBoolean(5)) featured = ReadFeaturedVote(reader);
            else votes.Add(ReadVote(reader));
        }
        return release is { } active ? new(active, featured, [.. votes], brief) : null;
    }

    private static HomeFeaturedVote ReadFeaturedVote(NpgsqlDataReader reader)
    {
        var result = reader.GetString(14);
        var billLine = ViewColumns.Text(reader, 19);
        return new(reader.GetString(6), reader.GetFieldValue<DateOnly>(7), ViewColumns.Enum<PublishedChamber>(reader, 8), reader.GetInt32(9), reader.GetInt32(10),
            reader.GetInt32(11), reader.GetString(12), ViewColumns.Text(reader, 13), reader.GetString(20), ViewColumns.Enum<PublishedTextOrigin>(reader, 21),
            result, PlainVoteWords.Result(result), ViewColumns.Number(reader, 15), ViewColumns.Number(reader, 16), ViewColumns.Text(reader, 17),
            ViewColumns.Number(reader, 18), billLine, billLine is null ? null : PublishedWording.BillLineSource);
    }

    private static HomeVote ReadVote(NpgsqlDataReader reader)
    {
        var result = reader.GetString(14);
        var billLine = ViewColumns.Text(reader, 19);
        return new(reader.GetString(6), reader.GetFieldValue<DateOnly>(7), ViewColumns.Enum<PublishedChamber>(reader, 8), reader.GetInt32(9), reader.GetInt32(10),
            reader.GetInt32(11), reader.GetString(12), ViewColumns.Text(reader, 13), result, PlainVoteWords.Result(result), ViewColumns.Number(reader, 15)!.Value,
            ViewColumns.Number(reader, 16)!.Value, ViewColumns.Text(reader, 17), ViewColumns.Number(reader, 18), billLine,
            billLine is null ? null : PublishedWording.BillLineSource);
    }
}
