using System.Data;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Votes;
using Npgsql;

namespace CivicLens.PublicApi.Home;

/// <summary>Reads home highlights and issue counts from one active-release snapshot.</summary>
public sealed class HomeStore(NpgsqlDataSource views)
{
    private const int VotesPerChamber = 2;
    private const string VoteColumns = """
        v.record_id, v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number,
        v.headline, v.label, v.result, v.yea, v.nay, v.bill_type, v.bill_number, v.summary, v.text_origin, v.alignment
        """;

    private static readonly string Sql = $"""
        SELECT a.release_number, a.published_at_utc_ticks, b.record_id, b.week_start, b.headline, b.label,
               s.is_featured, s.record_id, s.voted_on, s.chamber, s.congress, s.session_number, s.roll_call_number,
               s.headline, s.label, s.result, s.yea, s.nay, s.bill_type, s.bill_number, s.bill_line, s.summary, s.text_origin, s.alignment,
               s.action_kind, s.action_count, s.newest_record_id, s.newest_title, s.newest_signed_on,
               freshness.house, freshness.senate
          FROM (SELECT release_number, published_at_utc_ticks FROM public_api.active_release LIMIT 1) a
          CROSS JOIN LATERAL (
               SELECT max(v.voted_on) FILTER (WHERE v.chamber = 'House') AS house,
                      max(v.voted_on) FILTER (WHERE v.chamber = 'Senate') AS senate
                 FROM public_api.votes v
          ) freshness
          LEFT JOIN LATERAL (
               SELECT v.record_id FROM public_api.votes v
                WHERE v.kind = 'Passage' AND v.bill_type IS NOT NULL AND v.bill_number IS NOT NULL
                ORDER BY {VoteStore.Order} LIMIT 1
          ) featured_key ON TRUE
          LEFT JOIN LATERAL (
               SELECT {VoteColumns} FROM public_api.votes v
                WHERE v.record_id = featured_key.record_id LIMIT 1
          ) featured ON TRUE
          LEFT JOIN LATERAL (SELECT * FROM public_api.briefs b ORDER BY b.week_start DESC, b.record_id LIMIT 1) b ON TRUE
          LEFT JOIN LATERAL (
               SELECT v.is_featured, v.record_id, v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number,
                      v.headline, v.label, v.result, v.yea, v.nay, v.bill_type, v.bill_number,
                      (SELECT l.bill_line FROM public_api.bills l
                        WHERE l.congress = v.congress AND l.bill_type = v.bill_type AND l.bill_number = v.bill_number
                        ORDER BY l.record_id LIMIT 1) AS bill_line,
                      v.summary, v.text_origin, v.alignment,
                      NULL::text AS action_kind, NULL::integer AS action_count, NULL::text AS newest_record_id,
                      NULL::text AS newest_title, NULL::date AS newest_signed_on
                 FROM LATERAL (
                      SELECT true AS is_featured, featured.* WHERE featured.record_id IS NOT NULL
                      UNION ALL
                      SELECT false AS is_featured, recent.*
                        FROM unnest(@chambers) AS c(chamber)
                       CROSS JOIN LATERAL (
                            SELECT payload.*
                              FROM (
                                   SELECT v.record_id, v.revision_number, v.voted_on, v.chamber, v.session_number, v.roll_call_number
                                     FROM public_api.votes v
                                    WHERE v.chamber = c.chamber AND v.kind = ANY(@kinds)
                                      AND (featured.record_id IS NULL OR v.record_id <> featured.record_id)
                                    ORDER BY {VoteStore.Order} OFFSET 0
                              ) k
                              CROSS JOIN LATERAL (
                                   SELECT {VoteColumns} FROM public_api.votes v
                                    WHERE v.record_id = k.record_id AND v.revision_number = k.revision_number AND v.yea IS NOT NULL
                                    LIMIT 1 OFFSET 0
                              ) payload
                             ORDER BY {VoteStore.Order.Replace("v.", "k.", StringComparison.Ordinal)} LIMIT @take) recent
                 ) v
               UNION ALL
               SELECT false, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
                      NULL, NULL,
                      e.kind, e.action_count, e.record_id, e.headline, e.signed_on
                 FROM (
                      SELECT kind, record_id, headline, signed_on,
                             (count(*) OVER (PARTITION BY kind))::integer AS action_count,
                             row_number() OVER (PARTITION BY kind ORDER BY signed_on DESC, record_id) AS newest
                        FROM public_api.executive_actions
                 ) e
                WHERE e.newest = 1
          ) s ON TRUE
         ORDER BY s.is_featured DESC NULLS LAST, s.voted_on DESC, s.chamber, s.session_number DESC, s.roll_call_number DESC, s.record_id, s.action_kind
        """;

    private const string IssuesSql = """
        SELECT i.issue_id, i.name, COALESCE(v.total, 0), COALESCE(b.total, 0)
          FROM public_api.release_issues i
          LEFT JOIN (
               SELECT issue_id, count(*) AS total FROM public_api.issue_votes
                WHERE congress = @congress AND kind = ANY(@kinds) GROUP BY issue_id
          ) v ON v.issue_id = i.issue_id
          LEFT JOIN (
               SELECT issue_id, count(*) AS total FROM public_api.bill_issues
                WHERE congress = @congress GROUP BY issue_id
          ) b ON b.issue_id = i.issue_id
         ORDER BY i.name, i.issue_id
        """;

    /// <summary>Reads the home answer. Null means there is no active release.</summary>
    public async Task<HomeAnswer?> ReadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        command.Parameters.AddWithValue("chambers", Enum.GetNames<PublishedChamber>());
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        command.Parameters.AddWithValue("take", VotesPerChamber);
        var votes = new List<HomeVote>();
        HomeFeaturedVote? featured = null;
        HomeBrief? brief = null;
        var executiveActions = new List<HomeExecutiveGroup>();
        DateOnly? newestHouseRollCall = null;
        DateOnly? newestSenateRollCall = null;
        int? release = null;
        DateOnly? publishedOn = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                publishedOn ??= reader.IsDBNull(1) ? null : DateOnly.FromDateTime(new DateTime(reader.GetInt64(1), DateTimeKind.Utc));
                newestHouseRollCall ??= reader.IsDBNull(29) ? null : reader.GetFieldValue<DateOnly>(29);
                newestSenateRollCall ??= reader.IsDBNull(30) ? null : reader.GetFieldValue<DateOnly>(30);
                brief ??= reader.IsDBNull(2) ? null : new(reader.GetString(2), reader.GetFieldValue<DateOnly>(3), reader.GetString(4), ViewColumns.Text(reader, 5));
                if (!reader.IsDBNull(24))
                {
                    executiveActions.Add(new(ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 24), reader.GetInt32(25), reader.GetString(26), reader.GetString(27), reader.GetFieldValue<DateOnly>(28)));
                    continue;
                }
                if (reader.IsDBNull(6)) continue;
                if (reader.GetBoolean(6)) featured = ReadFeaturedVote(reader);
                else votes.Add(ReadVote(reader));
            }
        }
        if (release is not { } active) return null;
        var issues = publishedOn is { } day && CongressCalendar.CongressOn(day) is { } congress
            ? await ReadIssuesAsync(connection, transaction, congress, cancellationToken)
            : Array.Empty<HomeIssue>();
        return new(active, featured, [.. votes], brief)
        {
            ExecutiveActions = [.. executiveActions],
            Issues = issues,
            NewestHouseRollCall = newestHouseRollCall,
            NewestSenateRollCall = newestSenateRollCall
        };
    }

    private static async Task<HomeIssue[]> ReadIssuesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(IssuesSql, connection, transaction);
        command.Parameters.AddWithValue("congress", congress);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        var issues = new List<HomeIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            issues.Add(new(reader.GetString(0), reader.GetString(1), congress, (int)reader.GetInt64(2), (int)reader.GetInt64(3)));
        return [.. issues];
    }

    private static HomeFeaturedVote ReadFeaturedVote(NpgsqlDataReader reader)
    {
        var result = reader.GetString(15);
        var billLine = ViewColumns.Text(reader, 20);
        return new(reader.GetString(7), reader.GetFieldValue<DateOnly>(8), ViewColumns.Enum<PublishedChamber>(reader, 9), reader.GetInt32(10), reader.GetInt32(11),
            reader.GetInt32(12), reader.GetString(13), ViewColumns.Text(reader, 14), reader.GetString(21), ViewColumns.Enum<PublishedTextOrigin>(reader, 22),
            result, PlainVoteWords.Result(result), ViewColumns.Number(reader, 16), ViewColumns.Number(reader, 17), ViewColumns.Text(reader, 18),
            ViewColumns.Number(reader, 19), billLine, billLine is null ? null : PublishedWording.BillLineSource);
    }

    private static HomeVote ReadVote(NpgsqlDataReader reader)
    {
        var result = reader.GetString(15);
        var billLine = ViewColumns.Text(reader, 20);
        return new(reader.GetString(7), reader.GetFieldValue<DateOnly>(8), ViewColumns.Enum<PublishedChamber>(reader, 9), reader.GetInt32(10), reader.GetInt32(11),
            reader.GetInt32(12), reader.GetString(13), ViewColumns.Text(reader, 14), result, PlainVoteWords.Result(result), ViewColumns.Number(reader, 16)!.Value,
            ViewColumns.Number(reader, 17)!.Value, ViewColumns.Text(reader, 18), ViewColumns.Number(reader, 19), billLine,
            billLine is null ? null : PublishedWording.BillLineSource)
        {
            Alignment = reader.IsDBNull(23) ? null : ViewColumns.Enum<PublishedVoteAlignment>(reader, 23)
        };
    }
}
