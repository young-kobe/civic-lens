using CivicLens.PublicApi.Votes;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// The reads of the record page of one official, each inside the caller's snapshot: the count of each record kind and one page of a kind.
/// Every kind sorts newest day first, then record ID, so a page ends at one <see cref="OfficialRecordCursor"/>. A page reads one row more than it shows, which tells whether another page follows.
/// The vote kind rule is <see cref="PublishedVoteKindExtensions.SubstantiveNames"/>, as on the official page.
/// </summary>
internal static class OfficialRecordQueries
{
    public const int PageSize = 25;

    // OFFSET 0 keeps the speaker lookup correlated through the security barrier views.
    private const string SpokenBy = """
        EXISTS (SELECT 1 FROM public_api.statement_speakers p
                 WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND p.official_id = @id OFFSET 0)
        """;

    // An undated statement sorts last. The key is the second day of year 1, because Npgsql sends DateOnly.MinValue (the first day) as -infinity, which no row matches.
    private const string StatementDay = "COALESCE(s.statement_date, DATE '0001-01-02')";

    // The bills tab count. The official page reads it alone for its "See all" link, so the link and the tab show one number.
    private const string BillCountSql = """
        SELECT count(*) FROM public_api.bill_sponsorships p
          JOIN public_api.bills b ON b.record_id = p.record_id AND b.revision_number = p.revision_number
         WHERE p.official_id = @id AND p.role = 'Sponsor'
        """;

    private const string CountsSql = $"""
        SELECT (SELECT count(*) FROM public_api.member_votes v WHERE v.official_id = @id AND v.kind = ANY(@kinds)),
               ({BillCountSql}),
               (SELECT count(*) FROM public_api.statements s WHERE {SpokenBy}),
               (SELECT count(*) FROM public_api.executive_actions e WHERE e.official_id = @id)
        """;

    private static readonly string VotesSql = $"""
        SELECT a.release_number, v.official_id, {MemberVoteStore.VoteColumns}
          FROM public_api.member_votes v
         CROSS JOIN (SELECT release_number FROM public_api.active_release LIMIT 1) a
         WHERE v.official_id = @id AND v.kind = ANY(@kinds){After("v.voted_on", "v.record_id")}
         ORDER BY v.voted_on DESC, v.record_id LIMIT @take
        """;

    private static readonly string BillsSql = $"""
        SELECT {OfficialPageQueries.BillColumns}
          FROM public_api.bill_sponsorships p
          JOIN public_api.bills b ON b.record_id = p.record_id AND b.revision_number = p.revision_number
          LEFT JOIN public_api.bill_status t ON t.record_id = b.record_id AND t.revision_number = b.revision_number
         WHERE p.official_id = @id AND p.role = 'Sponsor'{After("p.sponsored_on", "b.record_id")}
         ORDER BY p.sponsored_on DESC, b.record_id LIMIT @take
        """;

    private static readonly string StatementsSql = $"""
        SELECT s.record_id, s.statement_date, s.headline, s.label, {StatementDay}
          FROM public_api.statements s
         WHERE {SpokenBy}{After(StatementDay, "s.record_id")}
         ORDER BY {StatementDay} DESC, s.record_id LIMIT @take
        """;

    private static readonly string ExecutiveSql = $"""
        SELECT e.record_id, e.headline, e.kind, e.signed_on
          FROM public_api.executive_actions e
         WHERE e.official_id = @id{After("e.signed_on", "e.record_id")}
         ORDER BY e.signed_on DESC, e.record_id LIMIT @take
        """;

    private static string After(string day, string recordId) => $" AND (@after_day::date IS NULL OR {day} < @after_day OR ({day} = @after_day AND {recordId} > @after_id))";

    /// <summary>Reads how many records the official has of each kind. A kind with no record counts zero.</summary>
    public static async Task<Dictionary<OfficialRecordKind, int>> ReadCountsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CountsSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new()
        {
            [OfficialRecordKind.Votes] = (int)reader.GetInt64(0),
            [OfficialRecordKind.Bills] = (int)reader.GetInt64(1),
            [OfficialRecordKind.Statements] = (int)reader.GetInt64(2),
            [OfficialRecordKind.ExecutiveActions] = (int)reader.GetInt64(3)
        };
    }

    /// <summary>Reads how many bills the official sponsored in the release, the count of the bills tab.</summary>
    public static async Task<int> ReadBillCountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BillCountSql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public static Task<Page<SharedVoteRowData>> ReadVotesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, OfficialRecordCursor? after,
        CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, VotesSql, officialId, after, reader =>
        {
            var vote = MemberVoteStore.ReadVote(reader);
            return (OfficialPageReader.Vote(officialId, vote) with { Chamber = vote.Chamber }, vote.VotedOn, vote.RecordId);
        }, cancellationToken, withKinds: true);

    public static Task<Page<OfficialBillData>> ReadBillsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, OfficialRecordCursor? after,
        CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, BillsSql, officialId, after, reader =>
        {
            var bill = OfficialPageQueries.ReadBill(reader);
            return (bill, bill.IntroducedOn, bill.RecordId);
        }, cancellationToken);

    public static Task<Page<OfficialRecordStatementData>> ReadStatementsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        OfficialRecordCursor? after, CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, StatementsSql, officialId, after, reader =>
        {
            var statement = new OfficialRecordStatementData(reader.GetString(0), ViewColumns.Date(reader, 1), reader.GetString(2), ViewColumns.Text(reader, 3));
            return (statement, reader.GetFieldValue<DateOnly>(4), statement.RecordId);
        }, cancellationToken);

    public static Task<Page<OfficialRecordExecutiveData>> ReadExecutiveActionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        OfficialRecordCursor? after, CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, ExecutiveSql, officialId, after, reader =>
        {
            var action = new OfficialRecordExecutiveData(reader.GetString(0), reader.GetString(1), ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 2),
                reader.GetFieldValue<DateOnly>(3));
            return (action, action.SignedOn, action.RecordId);
        }, cancellationToken);

    private static async Task<Page<T>> ReadPageAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, string officialId, OfficialRecordCursor? after,
        Func<NpgsqlDataReader, (T Row, DateOnly Day, string RecordId)> read, CancellationToken cancellationToken, bool withKinds = false)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("take", PageSize + 1);
        command.Parameters.AddWithValue("after_day", NpgsqlTypes.NpgsqlDbType.Date, after is null ? DBNull.Value : after.Day);
        command.Parameters.AddWithValue("after_id", after?.RecordId ?? string.Empty);
        if (withKinds) command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        var rows = new List<(T Row, DateOnly Day, string RecordId)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(read(reader));
        var shown = rows.Take(PageSize).ToArray();
        return new([.. shown.Select(row => row.Row)], rows.Count > PageSize ? (shown[^1].Day, shown[^1].RecordId) : null);
    }

    /// <summary>One page of rows and the sort key of its last row when another page follows.</summary>
    public sealed record Page<T>(T[] Rows, (DateOnly Day, string RecordId)? Next);
}
