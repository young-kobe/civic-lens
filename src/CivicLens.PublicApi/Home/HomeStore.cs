using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Votes;
using Npgsql;

namespace CivicLens.PublicApi.Home;

/// <summary>Reads the views <c>public_api.votes</c>, <c>public_api.bills</c>, and <c>public_api.briefs</c> for the home card. One statement reads the release, the votes, and the brief, so all three come from one release.
/// The kind rule is <see cref="PublishedVoteKindExtensions.SubstantiveNames"/>.</summary>
public sealed class HomeStore(NpgsqlDataSource views)
{
    private const int VotesPerChamber = 2;

    private const string Sql = $"""
        SELECT a.release_number, b.record_id, b.week_start, b.headline, b.label,
               v.record_id, v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number, v.headline, v.label, v.result, v.yea, v.nay,
               v.bill_type, v.bill_number, v.bill_line
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (SELECT * FROM public_api.briefs b ORDER BY b.week_start DESC, b.record_id LIMIT 1) b ON TRUE
          LEFT JOIN LATERAL (
               SELECT v.*, (SELECT l.bill_line FROM public_api.bills l
                             WHERE l.congress = v.congress AND l.bill_type = v.bill_type AND l.bill_number = v.bill_number
                             ORDER BY l.record_id LIMIT 1) AS bill_line
                 FROM unnest(@chambers) AS c(chamber)
                CROSS JOIN LATERAL (
                     SELECT v.* FROM public_api.votes v
                      WHERE v.chamber = c.chamber AND v.kind = ANY(@kinds) AND v.yea IS NOT NULL
                      ORDER BY {VoteStore.Order} LIMIT @take) v) v ON TRUE
         ORDER BY {VoteStore.Order}
        """;

    /// <summary>Reads the card. Null means that no release is active.</summary>
    public async Task<HomeAnswer?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("chambers", Enum.GetNames<PublishedChamber>());
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        command.Parameters.AddWithValue("take", VotesPerChamber);
        var votes = new List<HomeVote>();
        HomeBrief? brief = null;
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            brief ??= reader.IsDBNull(1) ? null : new(reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), ViewColumns.Text(reader, 4));
            if (!reader.IsDBNull(5)) votes.Add(ReadVote(reader));
        }
        return release is { } active ? new(active, [.. votes], brief) : null;
    }

    private static HomeVote ReadVote(NpgsqlDataReader reader)
    {
        var result = reader.GetString(13);
        var billLine = ViewColumns.Text(reader, 18);
        return new(reader.GetString(5), reader.GetFieldValue<DateOnly>(6), ViewColumns.Enum<PublishedChamber>(reader, 7), reader.GetInt32(8), reader.GetInt32(9),
            reader.GetInt32(10), reader.GetString(11), ViewColumns.Text(reader, 12), result, PlainVoteWords.Result(result), ViewColumns.Number(reader, 14)!.Value,
            ViewColumns.Number(reader, 15)!.Value, ViewColumns.Text(reader, 16), ViewColumns.Number(reader, 17), billLine, billLine is null ? null : PublishedWording.BillLineSource);
    }
}
