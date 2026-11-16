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
    // OFFSET 0 keeps the bill check correlated, as in SpokenBy.
    private const string BillCountSql = """
        SELECT count(*) FROM public_api.bill_sponsorships p
         WHERE p.official_id = @id AND p.role = 'Sponsor'
           AND EXISTS (SELECT 1 FROM public_api.bills b WHERE b.record_id = p.record_id AND b.revision_number = p.revision_number OFFSET 0)
        """;

    private const string CountsSql = $"""
        SELECT (SELECT count(*) FROM public_api.member_votes v WHERE v.official_id = @id AND v.kind = ANY(@kinds)),
               ({BillCountSql}),
               (SELECT count(*) FROM public_api.statements s WHERE {SpokenBy}),
               (SELECT count(*) FROM public_api.executive_actions e WHERE e.official_id = @id)
        """;

    private static string VotesSql(OfficialRecordRequest filter, bool backward) => $"""
        SELECT a.release_number, v.official_id, {MemberVoteStore.VoteColumns}
          FROM public_api.member_votes v
         CROSS JOIN (SELECT release_number FROM public_api.active_release LIMIT 1) a
         WHERE v.official_id = @id AND v.kind = ANY(@kinds){VoteFilters(filter)}{Window("v.voted_on", "v.record_id", backward)}
         ORDER BY {Order("v.voted_on", "v.record_id", backward)} LIMIT @take
        """;

    // The issue test reads the column issue_ids of the same view, as MemberVoteStore does. The party test reads breaks_party, the one owner of that rule,
    // and the cast test reads side, the normalized Yea or Nay of the view. Each filter only narrows the votes of one official, which the person index finds.
    private static string VoteFilters(OfficialRecordRequest filter) =>
        (filter.Issue is null ? "" : " AND @issue = ANY(v.issue_ids)") +
        (filter.BillType is null ? "" : " AND v.bill_type = @bill_type") +
        (filter.BillNumber is null ? "" : " AND v.bill_number = @bill_number") +
        (filter.AgainstPartyOnly ? " AND v.breaks_party" : "") +
        (filter.Cast is null ? "" : " AND v.side = @side");

    private static string BillsSql(bool backward) => $"""
        SELECT {OfficialPageQueries.BillColumns}, {OfficialPageQueries.BillDay}
        {OfficialPageQueries.SponsoredBillsSql}{Window(OfficialPageQueries.BillDay, "b.record_id", backward)}
         ORDER BY {Order(OfficialPageQueries.BillDay, "b.record_id", backward)} LIMIT @take
        """;

    private static string StatementsSql(bool backward) => $"""
        SELECT s.record_id, s.statement_date, s.headline, s.label, {StatementDay}
          FROM public_api.statements s
         WHERE {SpokenBy}{Window(StatementDay, "s.record_id", backward)}
         ORDER BY {Order(StatementDay, "s.record_id", backward)} LIMIT @take
        """;

    private static string ExecutiveSql(bool backward) => $"""
        SELECT e.record_id, e.headline, e.kind, e.signed_on
          FROM public_api.executive_actions e
         WHERE e.official_id = @id{Window("e.signed_on", "e.record_id", backward)}
         ORDER BY {Order("e.signed_on", "e.record_id", backward)} LIMIT @take
        """;

    // Every kind sorts by its day, newest first, then by record ID.
    private static string Order(string day, string recordId, bool backward) => KeysetPage.Order($"{day} DESC, {recordId}", backward);

    // A first page sends a null day, which passes every row. A previous page always has a cursor.
    private static string Window(string day, string recordId, bool backward)
    {
        var after = $"{day} < @after_day OR ({day} = @after_day AND {recordId} > @after_id)";
        return backward ? KeysetPage.Window(after, recordId, "@after_id", backward) : $" AND (@after_day::date IS NULL OR {after})";
    }

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

    /// <summary>Reads one page of the substantive votes of the official that pass the vote filters of the request.</summary>
    public static Task<Page<SharedVoteRowData>> ReadVotesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, OfficialRecordRequest filter,
        PageStart start, CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, VotesSql(filter, start.Backward), officialId, start, reader =>
        {
            var vote = MemberVoteStore.ReadVote(reader);
            return (OfficialPageReader.Vote(officialId, vote) with { Chamber = vote.Chamber }, vote.VotedOn, vote.RecordId);
        }, cancellationToken, parameters =>
        {
            parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
            if (filter.Issue is not null) parameters.AddWithValue("issue", filter.Issue);
            if (filter.BillType is not null) parameters.AddWithValue("bill_type", filter.BillType);
            if (filter.BillNumber is { } number) parameters.AddWithValue("bill_number", number);
            if (filter.Cast is not null) parameters.AddWithValue("side", filter.Cast == OfficialRecordRequest.Yes ? "Yea" : "Nay");
        });

    public static Task<Page<OfficialBillData>> ReadBillsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, PageStart start,
        CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, BillsSql(start.Backward), officialId, start, reader =>
        {
            var bill = OfficialPageQueries.ReadBill(reader);
            return (bill, reader.GetFieldValue<DateOnly>(7), bill.RecordId);
        }, cancellationToken);

    public static Task<Page<OfficialRecordStatementData>> ReadStatementsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        PageStart start, CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, StatementsSql(start.Backward), officialId, start, reader =>
        {
            var statement = new OfficialRecordStatementData(reader.GetString(0), ViewColumns.Date(reader, 1), reader.GetString(2), ViewColumns.Text(reader, 3));
            return (statement, reader.GetFieldValue<DateOnly>(4), statement.RecordId);
        }, cancellationToken);

    public static Task<Page<OfficialRecordExecutiveData>> ReadExecutiveActionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId,
        PageStart start, CancellationToken cancellationToken) =>
        ReadPageAsync(connection, transaction, ExecutiveSql(start.Backward), officialId, start, reader =>
        {
            var action = new OfficialRecordExecutiveData(reader.GetString(0), reader.GetString(1), ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 2),
                reader.GetFieldValue<DateOnly>(3));
            return (action, action.SignedOn, action.RecordId);
        }, cancellationToken);

    private static async Task<Page<T>> ReadPageAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, string officialId, PageStart start,
        Func<NpgsqlDataReader, (T Row, DateOnly Day, string RecordId)> read, CancellationToken cancellationToken, Action<NpgsqlParameterCollection>? filters = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("take", PageSize + 1);
        command.Parameters.AddWithValue("after_day", NpgsqlTypes.NpgsqlDbType.Date, start.Cursor is { } cursor ? cursor.Day : DBNull.Value);
        command.Parameters.AddWithValue("after_id", start.Cursor?.RecordId ?? string.Empty);
        filters?.Invoke(command.Parameters);
        var rows = new List<(T Row, DateOnly Day, string RecordId)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(read(reader));
        var (shown, next, previous) = KeysetPage.Cut(rows, PageSize, start.Cursor?.Encode(), start.Backward,
            row => new OfficialRecordCursor(start.Release, row.Day, row.RecordId).Encode());
        return new([.. shown.Select(row => row.Row)], next, previous);
    }

    /// <summary>Where a page starts: the release of the page, the cursor of the tab or null on the first page, and whether the cursor starts the previous page.</summary>
    public sealed record PageStart(int Release, OfficialRecordCursor? Cursor, bool Backward);

    /// <summary>One page of rows and the cursors of the next and the previous page.</summary>
    public sealed record Page<T>(T[] Rows, string? Next, string? Previous);
}
