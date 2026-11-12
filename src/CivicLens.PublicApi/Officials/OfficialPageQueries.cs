using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// The reads that only the official page needs, each inside the caller's snapshot: the vote strip marks, the statements that a reviewer linked to a vote,
/// the sponsored bills with their introduced dates, and the newest executive actions of each document type.
/// The marks read <c>member_votes.breaks_party</c>, the one owner of the party-break rule that <c>official_party_lines</c> also reads, so the strip and the figures count the same votes.
/// </summary>
internal static class OfficialPageQueries
{
    public const int MarkLimit = 12;
    public const int RowLimit = 5;

    // A vote that broke from the party (the view column breaks_party), or a roll call with the cast Not Voting. Newest first. The window count is the number of marks before the limit.
    private const string MarksSql = """
        SELECT v.record_id, v.voted_on, v.chamber, v.roll_call_number, v.vote_cast = @not_voting, v.party, count(*) OVER ()
          FROM public_api.member_votes v
         WHERE v.official_id = @id AND v.congress = @congress
           AND (v.vote_cast = @not_voting OR v.breaks_party)
         ORDER BY v.voted_on DESC, v.chamber, v.roll_call_number DESC, v.record_id
         LIMIT @take
        """;

    // OFFSET 0 keeps the speaker lookup and the vote link correlated through the security barrier views.
    private const string StatementsSql = """
        SELECT s.record_id, s.statement_date, s.headline, s.label, v.record_id, v.headline, v.label, v.voted_on, v.chamber, v.roll_call_number, m.vote_cast,
               s.text_origin, v.text_origin
          FROM public_api.statements s
         CROSS JOIN LATERAL (
              SELECT v.record_id, v.headline, v.label, v.voted_on, v.chamber, v.roll_call_number, v.text_origin
                FROM public_api.record_links l
                JOIN public_api.votes v ON v.record_id = l.related_record_id
               WHERE l.kind = 'statement' AND l.record_id = s.record_id AND l.related_kind = 'vote'
               ORDER BY v.voted_on DESC, v.record_id LIMIT 1 OFFSET 0) v
          LEFT JOIN public_api.member_votes m ON m.record_id = v.record_id AND m.official_id = @id
         WHERE EXISTS (SELECT 1 FROM public_api.statement_speakers p
                        WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND p.official_id = @id OFFSET 0)
         ORDER BY s.statement_date DESC NULLS LAST, s.record_id
         LIMIT @take
        """;

    // The message "no reviewed links" must stay true, so a statement linked to a bill counts as a link too. A bill is the record kind action.
    private const string BillLinkedStatementSql = """
        SELECT EXISTS (
            SELECT 1 FROM public_api.statements s
             WHERE EXISTS (SELECT 1 FROM public_api.statement_speakers p
                            WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND p.official_id = @id OFFSET 0)
               AND EXISTS (SELECT 1 FROM public_api.record_links l
                            WHERE l.kind = 'statement' AND l.record_id = s.record_id AND l.related_kind = 'action' OFFSET 0))
        """;

    private const string BillsSql = """
        SELECT b.record_id, b.bill_type, b.bill_line, b.bill_title, p.sponsored_on, t.latest_action_text, t.latest_action_date
          FROM public_api.bill_sponsorships p
          JOIN public_api.bills b ON b.record_id = p.record_id AND b.revision_number = p.revision_number
          LEFT JOIN public_api.bill_status t ON t.record_id = b.record_id AND t.revision_number = b.revision_number
         WHERE p.official_id = @id AND p.role = 'Sponsor' AND b.congress = @congress
         ORDER BY p.sponsored_on DESC, b.record_id
         LIMIT @take
        """;

    private const string ExecutiveSql = """
        SELECT k.kind, e.record_id, e.headline, e.signed_on
          FROM unnest(@kinds) AS k(kind)
         CROSS JOIN LATERAL (
              SELECT e.record_id, e.headline, e.signed_on FROM public_api.executive_actions e
               WHERE e.official_id = @id AND e.kind = k.kind
               ORDER BY e.signed_on DESC, e.record_id LIMIT @take OFFSET 0) e
         ORDER BY k.kind, e.signed_on DESC, e.record_id
        """;

    private const string ExecutiveSpanSql = "SELECT min(signed_on), max(signed_on) FROM public_api.executive_actions WHERE official_id = @id";

    /// <summary>Reads the newest marks and how many marks the official has in all.</summary>
    public static async Task<(OfficialMarkData[] Marks, int Total)> ReadMarksAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(MarksSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("congress", congress);
        command.Parameters.AddWithValue("not_voting", PublishedOfficialVote.NotVoting);
        command.Parameters.AddWithValue("take", MarkLimit);
        var marks = new List<OfficialMarkData>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            marks.Add(new(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), ViewColumns.Enum<PublishedChamber>(reader, 2), reader.GetInt32(3),
                reader.GetBoolean(4), ViewColumns.Text(reader, 5)));
            total = (int)reader.GetInt64(6);
        }
        return ([.. marks], total);
    }

    public static async Task<OfficialStatementData[]> ReadStatementsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(StatementsSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("take", RowLimit);
        var statements = new List<OfficialStatementData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            statements.Add(new(reader.GetString(0), ViewColumns.Date(reader, 1), reader.GetString(2), ViewColumns.Text(reader, 3), reader.GetString(4),
                reader.GetString(5), ViewColumns.Text(reader, 6), reader.GetFieldValue<DateOnly>(7), ViewColumns.Enum<PublishedChamber>(reader, 8),
                reader.GetInt32(9), ViewColumns.Text(reader, 10))
            {
                TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 11),
                VoteTextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 12)
            });
        return [.. statements];
    }

    public static async Task<bool> ReadHasBillLinkedStatementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BillLinkedStatementSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public static async Task<OfficialBillData[]> ReadBillsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BillsSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("congress", congress);
        command.Parameters.AddWithValue("take", RowLimit);
        var bills = new List<OfficialBillData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            bills.Add(new(reader.GetString(0), reader.GetString(1), ViewColumns.Text(reader, 2), reader.GetString(3), reader.GetFieldValue<DateOnly>(4),
                ViewColumns.Text(reader, 5), ViewColumns.Date(reader, 6)));
        return [.. bills];
    }

    /// <summary>Reads the newest actions of each document type and the signing span. The counts come from the official figure, so every number has one owner.</summary>
    public static async Task<OfficialExecutiveData?> ReadExecutiveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        ExecutiveActionsFigure figure, CancellationToken cancellationToken)
    {
        var kinds = figure.ByDocumentType.Select(count => KindOf(count.DocumentType)).ToArray();
        if (kinds.Length == 0) return null;
        var newest = await ReadNewestActionsAsync(connection, transaction, officialId, kinds, cancellationToken);
        if (await ReadSpanAsync(connection, transaction, officialId, cancellationToken) is not var (first, last)) return null;
        var types = figure.ByDocumentType.Zip(kinds, (count, kind) => new OfficialExecutiveType(kind, count.Actions, [.. newest[kind]]));
        return new(figure.ByDocumentType.Sum(count => count.Actions), first, last, [.. types.OrderBy(type => type.Kind)]);
    }

    private static async Task<Dictionary<PublishedExecutiveActionKind, List<OfficialExecutiveRow>>> ReadNewestActionsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string officialId, PublishedExecutiveActionKind[] kinds, CancellationToken cancellationToken)
    {
        var rows = kinds.ToDictionary(kind => kind, _ => new List<OfficialExecutiveRow>());
        await using var command = new NpgsqlCommand(ExecutiveSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("kinds", kinds.Select(kind => kind.ToString()).ToArray());
        command.Parameters.AddWithValue("take", RowLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows[ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 0)].Add(new(reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateOnly>(3)));
        return rows;
    }

    private static PublishedExecutiveActionKind KindOf(string documentType) =>
        Enum.TryParse<PublishedExecutiveActionKind>(documentType, out var kind) && Enum.IsDefined(kind)
            ? kind
            : throw new InvalidDataException("The executive action figure holds an unknown document type.");

    private static async Task<(DateOnly First, DateOnly Last)?> ReadSpanAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ExecutiveSpanSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0)
            ? (reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1))
            : null;
    }
}
