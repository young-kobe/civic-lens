using System.Data.Common;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Statements;

public sealed class StatementStore(NpgsqlDataSource views)
{
    private const string DetailSql = """
        SELECT a.release_number, s.record_id, s.revision_number, s.statement_date, s.headline, s.summary, s.text_origin, s.label,
               ARRAY(SELECT p.official_id FROM public_api.statement_speakers p
                      WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number ORDER BY p."position"),
               s.issue_ids, src.extraction_id, src.url, src.observed_at_utc_ticks, src.text, src.article_url,
               src.statement_date_evidence_extraction_id, src.statement_date_evidence_start, src.statement_date_evidence_length,
               (SELECT json_agg(json_build_object('extractionId', q.extraction_id,
                    'start', q.quotation_start, 'length', q.quotation_length, 'kind', q.kind) ORDER BY q.position)
                  FROM (SELECT record_id, revision_number, position, extraction_id, quotation_start, quotation_length, kind
                          FROM public_api.statement_quotations
                         WHERE record_id = s.record_id AND revision_number = s.revision_number ORDER BY position LIMIT @quotation_limit) q),
               ap.approved_by
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.statements s ON s.record_id = @id
          LEFT JOIN public_api.statement_sources src ON src.record_id = s.record_id AND src.revision_number = s.revision_number
          LEFT JOIN public_api.statement_approvers ap ON ap.record_id = s.record_id AND ap.revision_number = s.revision_number
        """;

    public Task<(int? Release, StatementDetail? Detail)> FindAsync(string recordId, CancellationToken cancellationToken) =>
        ReadDetailAsync(views.CreateCommand(DetailSql), recordId, cancellationToken);

    /// <summary>Reads the detail inside the transaction of the caller, so that it shares a snapshot with the other reads of that transaction.</summary>
    public Task<(int? Release, StatementDetail? Detail)> FindAsync(NpgsqlTransaction transaction, string recordId, CancellationToken cancellationToken) =>
        ReadDetailAsync(new NpgsqlCommand(DetailSql, transaction.Connection, transaction), recordId, cancellationToken);

    private static async Task<(int? Release, StatementDetail? Detail)> ReadDetailAsync(NpgsqlCommand command, string recordId, CancellationToken cancellationToken)
    {
        await using var _ = command;
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("quotation_limit", PublishedStatement.MaximumQuotations + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return (null, null);
        if (reader.IsDBNull(1)) return (reader.GetInt32(0), null);
        if (reader.IsDBNull(10) || reader.IsDBNull(11) || reader.IsDBNull(12) || reader.IsDBNull(13) || reader.IsDBNull(18) || reader.IsDBNull(19))
            throw new InvalidDataException("The active statement has no published evidence content.");

        var statement = ReadRow(reader);
        var source = new PublishedDocumentVersion
        {
            ExtractionId = reader.GetString(10),
            Url = reader.GetString(11),
            ObservedAtUtc = new DateTimeOffset(reader.GetInt64(12), TimeSpan.Zero),
            Text = reader.GetString(13)
        };
        if (source.Text.Length > PublishedDocumentVersion.MaximumTextLength)
            throw new InvalidDataException("Published source text exceeds its maximum length.");
        var rows = JsonSerializer.Deserialize<StatementDetailRow[]>(reader.GetString(18), PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("The statement quotations view returned invalid content.");
        if (rows.Length is 0 or > PublishedStatement.MaximumQuotations)
            throw new InvalidDataException($"Published statement requires 1 to {PublishedStatement.MaximumQuotations} quotations.");
        var quotations = rows.Select(row => new PublishedQuotation
        {
            ExtractionId = row.ExtractionId,
            Start = row.Start,
            Length = row.Length,
            Quote = QuoteAt(source.Text, row.Start, row.Length),
            Kind = Enum.TryParse<PublishedQuotationKind>(row.Kind, out var kind) && Enum.IsDefined(kind)
                ? kind
                : throw new InvalidDataException("The statement quotations view holds an unknown kind.")
        }).ToArray();
        var dateEvidence = ReadDateEvidence(reader, source.Text);
        var articleUrl = ViewColumns.Text(reader, 14);
        PublishedStatement.ValidateContent(source, quotations, dateEvidence, articleUrl);
        return (reader.GetInt32(0), new(reader.GetInt32(0), statement, ViewColumns.Enum<PublishedApprover>(reader, 19), source, articleUrl, dateEvidence, quotations));
    }

    public async Task<StatementPage?> ListAsync(StatementRequest request, CancellationToken cancellationToken) =>
        (await ReadAsync(views.CreateCommand(ListSql(request)), request, withTotal: false, cancellationToken))?.Page;

    /// <summary>Reads the page and the number of statements of the active release inside the transaction of the caller, so that it shares a snapshot
    /// with the other reads of that transaction. A filter never changes the total.</summary>
    public async Task<(StatementPage Page, int StatementsInRelease)?> ListWithTotalAsync(NpgsqlTransaction transaction, StatementRequest request,
        CancellationToken cancellationToken) =>
        await ReadAsync(new NpgsqlCommand(ListSql(request, withTotal: true), transaction.Connection, transaction), request, withTotal: true, cancellationToken) is { } read
            ? (read.Page, read.Total!.Value)
            : null;

    // OFFSET 0 keeps the link lookup correlated through the security barrier view.
    private const string ListFactsSql = """
        SELECT s.record_id, src.url,
               EXISTS (SELECT 1 FROM public_api.record_links l
                        WHERE l.kind = 'statement' AND l.record_id = s.record_id AND l.related_kind = 'vote' OFFSET 0)
          FROM public_api.statements s
          JOIN public_api.statement_sources src ON src.record_id = s.record_id AND src.revision_number = s.revision_number
         WHERE s.record_id = ANY(@ids)
        """;

    /// <summary>Reads the source site and the vote link of a bounded set of statements in one query, inside the transaction of the caller.
    /// A statement that left the release has no entry.</summary>
    public async Task<Dictionary<string, StatementListFacts>> ReadListFactsAsync(NpgsqlTransaction transaction, string[] recordIds, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ListFactsSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("ids", recordIds);
        var facts = new Dictionary<string, StatementListFacts>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            facts[reader.GetString(0)] = new(reader.GetString(1), reader.GetBoolean(2));
        return facts;
    }

    private static string ListSql(StatementRequest request, bool withTotal = false) => $"""
        SELECT a.release_number, s.record_id, s.revision_number, s.statement_date, s.headline, s.summary, s.text_origin, s.label,
               ARRAY(SELECT p.official_id FROM public_api.statement_speakers p
                      WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number ORDER BY p."position"),
               s.issue_ids{(withTotal ? ", (SELECT count(*) FROM public_api.statements) AS in_release" : "")}
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT s.* FROM public_api.statements s
                WHERE TRUE{Filters(request)}{Cursor(request)}
                ORDER BY s.statement_date DESC NULLS LAST, s.record_id LIMIT @take) s ON TRUE
         ORDER BY s.statement_date DESC NULLS LAST, s.record_id
        """;

    private static async Task<(StatementPage Page, int? Total)?> ReadAsync(NpgsqlCommand command, StatementRequest request, bool withTotal, CancellationToken cancellationToken)
    {
        await using var _ = command;
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Official is not null) command.Parameters.AddWithValue("official", request.Official);
        if (request.SpeakerName is not null) command.Parameters.AddWithValue("name_prefix", request.SpeakerName);
        if (request.Issue is not null) command.Parameters.AddWithValue("issue", request.Issue);
        if (request.From is { } from) command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is { } cursor)
        {
            command.Parameters.AddWithValue("cursor_id", cursor.RecordId);
            if (cursor.StatementDate is { } date) command.Parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = date });
        }
        var rows = new List<StatementRow>();
        int? release = null, total = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (withTotal) total = ViewColumns.Total(reader);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? StatementCursor.Of(active, page[^1]).Encode() : null;
        return (new(active, page, next), total);
    }

    private const string AfterDated = " AND (s.statement_date < @cursor_date OR s.statement_date IS NULL OR (s.statement_date = @cursor_date AND s.record_id > @cursor_id))";
    private const string AfterUndated = " AND s.statement_date IS NULL AND s.record_id > @cursor_id";

    private static string Cursor(StatementRequest request) => request.Cursor switch
    {
        null => "",
        { StatementDate: null } => AfterUndated,
        _ => AfterDated
    };
    // OFFSET 0 keeps the speaker lookup correlated through the security barrier view.
    private static string Filters(StatementRequest request) =>
        (request.Official is null ? "" : " AND EXISTS (SELECT 1 FROM public_api.statement_speakers p WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND p.official_id = @official OFFSET 0)") +
        (request.SpeakerName is null ? "" : $" AND EXISTS (SELECT 1 FROM public_api.statement_speakers p JOIN public_api.officials o ON o.official_id = p.official_id WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND {OfficialStore.NameMatchSql("o")} OFFSET 0)") +
        (request.Issue is null ? "" : " AND @issue = ANY(s.issue_ids)") +
        (request.From is null ? "" : " AND s.statement_date >= @from") +
        (request.To is null ? "" : " AND s.statement_date <= @to");

    private static StatementRow ReadRow(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        StatementDate = ViewColumns.Date(reader, 3),
        Headline = reader.GetString(4),
        Summary = reader.GetString(5),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 6),
        Label = ViewColumns.Text(reader, 7),
        SpeakerIds = reader.GetFieldValue<string[]>(8),
        IssueIds = reader.GetFieldValue<string[]>(9)
    };

    private static PublishedCitation? ReadDateEvidence(DbDataReader reader, string sourceText)
    {
        if (reader.IsDBNull(15) && reader.IsDBNull(16) && reader.IsDBNull(17)) return null;
        if (reader.IsDBNull(15) || reader.IsDBNull(16) || reader.IsDBNull(17))
            throw new InvalidDataException("The published statement date evidence is incomplete.");
        var start = reader.GetInt32(16);
        var length = reader.GetInt32(17);
        return new()
        {
            ExtractionId = reader.GetString(15),
            Start = start,
            Length = length,
            Quote = QuoteAt(sourceText, start, length)
        };
    }

    private static string QuoteAt(string text, int start, int length) =>
        start >= 0 && length > 0 && start <= text.Length - length
            ? text.Substring(start, length)
            : throw new InvalidDataException("Published citation quote does not match its offsets.");
}
