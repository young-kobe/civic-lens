using System.Data.Common;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Search;

/// <summary>Reads the index. Every read names a release, so a pinned read sees the revisions that release held.</summary>
public sealed class SearchIndex(NpgsqlDataSource database)
{
    private const int SnippetScanCharacters = 50_000;
    private const string SnippetOptions = "StartSel=, StopSel=, MaxFragments=1, MaxWords=25, MinWords=10";
    private const string SortDate = "COALESCE(r.event_date, (r.first_published_at_utc AT TIME ZONE 'UTC')::date)";
    private const string InRelease = "r.first_release <= @release AND (r.last_release IS NULL OR r.last_release >= @release)";
    private const string RankedAfterCursor = "WHERE r.rank < @cursor_rank OR (r.rank = @cursor_rank AND r.record_id > @cursor_id)";
    private const string DatedAfterCursor = $"""
         AND ({SortDate} < @cursor_date OR ({SortDate} = @cursor_date AND (r.first_published_at_utc < @cursor_published
            OR (r.first_published_at_utc = @cursor_published AND r.record_id > @cursor_id))))
        """;
    private const string Columns = """
        r.record_id, r.revision_number, r.kind, r.headline, r.first_published_at_utc, r.officials, r.issue_ids, r.policy_area_ids,
        r.source_host, r.event_date, r.first_release, r.last_release
        """;

    /// <summary>Finds the release with the given number, or the active one when the number is null. Null means that none is indexed.</summary>
    public async Task<ReleaseInfo?> FindReleaseAsync(int? number, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(
            "SELECT release_number, published_at_utc FROM releases WHERE release_number = COALESCE(@number, (SELECT release_number FROM active_release))");
        command.Parameters.Add(new NpgsqlParameter("number", NpgsqlDbType.Integer) { Value = (object?)number ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetInt32(0), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }

    public async Task<RecordHit?> FindRecordAsync(string recordId, int release, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand($"SELECT {Columns}, NULL::text FROM records r WHERE r.record_id = @id AND {InRelease}");
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("release", release);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHit(reader) : null;
    }

    public async Task<SearchPage> SearchAsync(int release, SearchRequest request, CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand(request switch
        {
            { IsRanked: true } => RankedSql(request),
            { Sort: { } sort } => SortedSql(request, sort),
            _ => DatedSql(request)
        });
        AddParameters(command, release, request);
        var rows = new List<(RecordHit Hit, float Rank)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                rows.Add((ReadHit(reader), request.IsRanked ? reader.GetFloat(13) : 0));
        var hasMore = rows.Count > request.Limit;
        var page = rows.Take(request.Limit).ToArray();
        return new(release, [.. page.Select(row => row.Hit)], hasMore ? NextCursor(release, request, page[^1]).Encode() : null);
    }

    private static string RankedSql(SearchRequest request) => $"""
        WITH q AS (SELECT websearch_to_tsquery('english', @q) AS query),
        ranked AS (
            SELECT r.*, ts_rank_cd(r.search, q.query) AS rank FROM records r CROSS JOIN q
            WHERE {InRelease} AND r.search @@ q.query{Filters(request)}
        ),
        page AS (
            SELECT * FROM ranked r {(request.Cursor is null ? "" : RankedAfterCursor)}
            ORDER BY r.rank DESC, r.record_id LIMIT @take
        )
        SELECT {Columns},
            ts_headline('english', left(r.body, {SnippetScanCharacters}), q.query, '{SnippetOptions}'), r.rank
        FROM page r CROSS JOIN q ORDER BY r.rank DESC, r.record_id
        """;

    private static string DatedSql(SearchRequest request) => $"""
        SELECT {Columns}, NULL::text FROM records r
        WHERE {InRelease}{Filters(request)}{(request.Cursor is null ? "" : DatedAfterCursor)}
        ORDER BY {SortDate} DESC, r.first_published_at_utc DESC, r.record_id LIMIT @take
        """;

    private static string SortedSql(SearchRequest request, SearchSortKey sort)
    {
        var key = SortKeyExpression(sort);
        var direction = request.Descending ? "DESC" : "ASC";
        var after = request.Cursor is null ? "" : $"""
             AND ({key}, r.record_id) {(request.Descending ? "<" : ">")}
                (SELECT {key}, r.record_id FROM records r WHERE r.record_id = @cursor_id AND {InRelease})
            """;
        return $"""
            SELECT {Columns}, NULL::text FROM records r
            WHERE {InRelease}{Filters(request)}{after}
            ORDER BY {key} {direction}, r.record_id {direction} LIMIT @take
            """;
    }

    private static string SortKeyExpression(SearchSortKey sort) => sort switch
    {
        SearchSortKey.Date => SortDate,
        SearchSortKey.Published => "r.first_published_at_utc",
        SearchSortKey.Headline => "lower(r.headline COLLATE pg_c_utf8) COLLATE \"C\"",
        SearchSortKey.Kind => "r.kind COLLATE \"C\"",
        _ => throw new ArgumentOutOfRangeException(nameof(sort), sort, "Sort key is not known.")
    };

    private static string Filters(SearchRequest request) =>
        (request.Text is not null && request.Sort is not null ? " AND r.search @@ websearch_to_tsquery('english', @q)" : "") +
        (request.Kind is null ? "" : " AND r.kind = @kind") +
        (request.Official is null ? "" : " AND r.official_ids @> ARRAY[@official]::text[]") +
        (request.Issue is null ? "" : " AND r.issue_ids @> ARRAY[@issue]::text[]") +
        (request.PolicyArea is null ? "" : " AND r.policy_area_ids @> ARRAY[@policy_area]::text[]") +
        (request.Source is null ? "" : " AND r.source_host = @source") +
        (request.From is null ? "" : " AND r.event_date >= @from") +
        (request.To is null ? "" : " AND r.event_date <= @to");

    private static void AddParameters(NpgsqlCommand command, int release, SearchRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("release", release);
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Text is not null) parameters.AddWithValue("q", request.Text);
        if (request.Kind is { } kind) parameters.AddWithValue("kind", PublicationProtocol.KindText(kind));
        if (request.Official is not null) parameters.AddWithValue("official", request.Official);
        if (request.Issue is not null) parameters.AddWithValue("issue", request.Issue);
        if (request.PolicyArea is not null) parameters.AddWithValue("policy_area", request.PolicyArea);
        if (request.Source is not null) parameters.AddWithValue("source", request.Source);
        if (request.From is { } from) parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is not { } cursor) return;
        parameters.AddWithValue("cursor_id", cursor.RecordId);
        if (cursor.Rank is { } rank) parameters.Add(new NpgsqlParameter("cursor_rank", NpgsqlDbType.Real) { Value = rank });
        if (cursor.SortDate is { } date) parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = date });
        if (cursor.PublishedTicks is { } ticks)
            parameters.Add(new NpgsqlParameter("cursor_published", NpgsqlDbType.TimestampTz) { Value = new DateTime(ticks, DateTimeKind.Utc) });
    }

    private static SearchCursor NextCursor(int release, SearchRequest request, (RecordHit Hit, float Rank) last) => request switch
    {
        { IsRanked: true } => SearchCursor.Ranked(release, last.Hit.RecordId, last.Rank),
        { Sort: not null } => SearchCursor.Sorted(release, last.Hit.RecordId),
        _ => SearchCursor.Dated(release, last.Hit.RecordId, last.Hit.EventDate ?? DateOnly.FromDateTime(last.Hit.FirstPublishedAtUtc.UtcDateTime), last.Hit.FirstPublishedAtUtc.UtcTicks)
    };

    private static RecordHit ReadHit(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(0),
        RevisionNumber = reader.GetInt32(1),
        Kind = RecordKinds.TryParse(reader.GetString(2), out var kind) ? kind : throw new InvalidDataException("The index holds an unknown record kind."),
        Headline = reader.GetString(3),
        FirstPublishedAtUtc = reader.GetFieldValue<DateTimeOffset>(4),
        Officials = JsonSerializer.Deserialize<PublishedOfficial[]>(reader.GetString(5), PublicationProtocol.JsonOptions)!,
        IssueIds = reader.GetFieldValue<string[]>(6),
        PolicyAreaIds = reader.GetFieldValue<string[]>(7),
        SourceHost = reader.IsDBNull(8) ? null : reader.GetString(8),
        EventDate = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
        FirstRelease = reader.GetInt32(10),
        LastRelease = reader.IsDBNull(11) ? null : reader.GetInt32(11),
        Snippet = reader.IsDBNull(12) ? null : reader.GetString(12)
    };
}
