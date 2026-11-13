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
        v.headline, v.label, v.result, v.yea, v.nay, v.bill_type, v.bill_number, v.summary, v.text_origin, v.alignment,
        v.bill_record_id, v.present, v.not_voting
        """;

    private static readonly string Sql = $"""
        SELECT a.release_number, b.record_id, b.week_start, b.headline, b.label,
               s.is_featured, s.record_id, s.voted_on, s.chamber, s.congress, s.session_number, s.roll_call_number,
               s.headline, s.label, s.result, s.yea, s.nay, s.bill_type, s.bill_number, s.bill_line, s.summary, s.text_origin, s.alignment,
               s.action_kind, s.action_count, s.newest_record_id, s.newest_title, s.newest_signed_on,
               freshness.house, freshness.senate, s.present, s.not_voting
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
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
                        WHERE l.record_id = v.bill_record_id) AS bill_line,
                      v.summary, v.text_origin, v.alignment,
                      NULL::text AS action_kind, NULL::integer AS action_count, NULL::text AS newest_record_id,
                      NULL::text AS newest_title, NULL::date AS newest_signed_on, v.present, v.not_voting
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
                      e.kind, e.action_count, e.record_id, e.headline, e.signed_on, NULL::integer, NULL::integer
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
        SELECT i.issue_id, i.name, i.count_congress, i.substantive_votes, i.bills
          FROM public_api.release_issues i
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
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                newestHouseRollCall ??= reader.IsDBNull(28) ? null : reader.GetFieldValue<DateOnly>(28);
                newestSenateRollCall ??= reader.IsDBNull(29) ? null : reader.GetFieldValue<DateOnly>(29);
                brief ??= reader.IsDBNull(1) ? null : new(reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), ViewColumns.Text(reader, 4));
                if (!reader.IsDBNull(23))
                {
                    executiveActions.Add(new(ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 23), reader.GetInt32(24), reader.GetString(25), reader.GetString(26), reader.GetFieldValue<DateOnly>(27)));
                    continue;
                }
                if (reader.IsDBNull(5)) continue;
                if (reader.GetBoolean(5)) featured = ReadFeaturedVote(reader);
                else votes.Add(ReadVote(reader));
            }
        }
        if (release is not { } active) return null;
        var issues = await ReadIssuesAsync(connection, transaction, cancellationToken);
        return new(active, featured, [.. votes], brief)
        {
            ExecutiveActions = [.. executiveActions],
            Issues = issues,
            NewestHouseRollCall = newestHouseRollCall,
            NewestSenateRollCall = newestSenateRollCall
        };
    }

    private static async Task<HomeIssue[]> ReadIssuesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(IssuesSql, connection, transaction);
        var issues = new List<HomeIssue>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2)) continue;
            issues.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), checked((int)reader.GetInt64(3)), checked((int)reader.GetInt64(4))));
        }
        return [.. issues];
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
            billLine is null ? null : PublishedWording.BillLineSource)
        {
            Alignment = reader.IsDBNull(22) ? null : ViewColumns.Enum<PublishedVoteAlignment>(reader, 22),
            Present = ViewColumns.Number(reader, 30),
            NotVoting = ViewColumns.Number(reader, 31)
        };
    }
}
