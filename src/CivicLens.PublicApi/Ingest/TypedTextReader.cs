using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Reads the texts that search indexes for each kind from the typed views, with the parts in the order of <see cref="RecordReader"/>.
/// Long source texts are cut in SQL with <c>left</c> and the limit constant of <see cref="IndexedRecord"/>.
/// The statement source and the relationship quote read the full text, because their offsets point into it.
/// <see cref="RecordReader.Create"/> stays the one owner of the text bound rule. A record with no row in its anchor view has no entry in the result.
/// </summary>
public sealed class TypedTextReader(NpgsqlTransaction snapshot)
{
    private static readonly string StatementSummary = $"SELECT x.record_id, x.summary FROM public_api.statements x WHERE {RevisionKeys.Filter}";
    private static readonly string StatementSource = $"SELECT x.record_id, x.text FROM public_api.statement_sources x WHERE {RevisionKeys.Filter}";

    private static readonly string StatementQuotations = $"""
        SELECT x.record_id, x.quotation_start, x.quotation_length FROM public_api.statement_quotations x WHERE {RevisionKeys.Filter}
         ORDER BY x.record_id, x."position"
        """;

    private static readonly string DocumentChangeSummary = $"SELECT x.record_id, x.summary FROM public_api.document_changes x WHERE {RevisionKeys.Filter}";

    // The new version comes first, as it does in the record file.
    private static readonly string DocumentChangeSources = $"""
        SELECT x.record_id, left(x.text, @limit) FROM public_api.document_change_sources x WHERE {RevisionKeys.Filter}
         ORDER BY x.record_id, CASE x.source_side WHEN 'After' THEN 0 ELSE 1 END
        """;

    private static readonly string ActionSummary = $"SELECT x.record_id, x.summary, x.bill_title FROM public_api.bills x WHERE {RevisionKeys.Filter}";

    private static readonly string ActionSources = $"""
        SELECT x.record_id, left(x.text, @limit) FROM public_api.action_sources x WHERE {RevisionKeys.Filter} ORDER BY x.record_id, x."position"
        """;

    private static readonly string VoteSummary = $"SELECT x.record_id, x.summary FROM public_api.votes x WHERE {RevisionKeys.Filter}";

    private static readonly string VoteSources = $"""
        SELECT x.record_id, left(x.text, @limit) FROM public_api.vote_sources x WHERE {RevisionKeys.Filter} ORDER BY x.record_id, x."position"
        """;

    private static readonly string ExecutiveActionSources = $"""
        SELECT x.record_id, left(x.text, @limit) FROM public_api.executive_action_sources x WHERE {RevisionKeys.Filter} ORDER BY x.record_id, x."position"
        """;

    private static readonly string BriefParagraphs = $"""
        SELECT x.record_id, x.paragraph_text FROM public_api.brief_paragraphs x WHERE {RevisionKeys.Filter} ORDER BY x.record_id, x."position"
        """;

    // The statement half of record_links names the relationship as link_record_id. The quote spans the source text of that statement.
    private const string RelationshipQuotes = """
        SELECT x.link_record_id, x.reason, s.text, x.quote_start, x.quote_length
          FROM public_api.record_links x
         CROSS JOIN LATERAL (
              SELECT s.text FROM public_api.statement_sources s
               WHERE s.record_id = x.record_id AND s.extraction_id = x.quote_extraction_id OFFSET 0) s
         WHERE x.kind = 'statement' AND x.link_record_id = ANY(@ids)
        """;

    public Task<Dictionary<string, RecordTexts>> ReadAsync(PublishedRecordKind kind, RevisionKeys keys, CancellationToken cancellationToken) => kind switch
    {
        PublishedRecordKind.Statement => ReadStatementsAsync(keys, cancellationToken),
        PublishedRecordKind.DocumentChange => ReadDocumentChangesAsync(keys, cancellationToken),
        PublishedRecordKind.Action => ReadActionsAsync(keys, cancellationToken),
        PublishedRecordKind.Vote => ReadVotesAsync(keys, cancellationToken),
        PublishedRecordKind.ExecutiveAction => ReadExecutiveActionsAsync(keys, cancellationToken),
        PublishedRecordKind.Brief => ReadBriefsAsync(keys, cancellationToken),
        PublishedRecordKind.Relationship => ReadRelationshipsAsync(keys, cancellationToken),
        _ => throw new InvalidDataException("The record kind is unknown.")
    };

    private async Task<Dictionary<string, RecordTexts>> ReadStatementsAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var summaries = await RowsAsync(keys, StatementSummary, cancellationToken);
        var sources = await RowsAsync(keys, StatementSource, cancellationToken);
        var spans = await SpansAsync(keys, cancellationToken);
        var texts = new Dictionary<string, RecordTexts>();
        foreach (var (id, rows) in summaries)
        {
            if (!sources.TryGetValue(id, out var source)) continue;
            var summary = rows[0][0]!;
            var quotations = spans.GetValueOrDefault(id, []).Select(span => QuoteOf(Text(source[0]), span.Start, span.Length));
            texts[id] = RecordTexts.Of(() => new([summary, .. quotations.ToArray()], [Text(source[0])], summary));
        }
        return texts;
    }

    private async Task<Dictionary<string, RecordTexts>> ReadDocumentChangesAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var summaries = await RowsAsync(keys, DocumentChangeSummary, cancellationToken);
        var sources = await RowsAsync(keys, DocumentChangeSources, cancellationToken);
        return Combine(summaries, sources, (summary, parts) => new([summary[0]!], parts, summary[0]));
    }

    private async Task<Dictionary<string, RecordTexts>> ReadActionsAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var summaries = await RowsAsync(keys, ActionSummary, cancellationToken);
        var sources = await RowsAsync(keys, ActionSources, cancellationToken);
        return Combine(summaries, sources, (summary, parts) => new([summary[0]!, summary[1]!], parts, summary[0]));
    }

    private async Task<Dictionary<string, RecordTexts>> ReadVotesAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var summaries = await RowsAsync(keys, VoteSummary, cancellationToken);
        var sources = await RowsAsync(keys, VoteSources, cancellationToken);
        return Combine(summaries, sources, (summary, parts) => new([summary[0]!], parts, summary[0]));
    }

    private async Task<Dictionary<string, RecordTexts>> ReadExecutiveActionsAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var sources = await RowsAsync(keys, ExecutiveActionSources, cancellationToken);
        return sources.ToDictionary(item => item.Key, item => RecordTexts.Of(() => new([], [.. item.Value.Select(row => Text(row))])));
    }

    private async Task<Dictionary<string, RecordTexts>> ReadBriefsAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var paragraphs = await RowsAsync(keys, BriefParagraphs, cancellationToken);
        return paragraphs.ToDictionary(item => item.Key, item => RecordTexts.Of(() => new([.. item.Value.Select(row => Text(row))], [])));
    }

    private async Task<Dictionary<string, RecordTexts>> ReadRelationshipsAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var texts = new Dictionary<string, RecordTexts>();
        await using var command = keys.Command(snapshot, RelationshipQuotes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string id = reader.GetString(0), reason = reader.GetString(1), text = reader.GetString(2);
            int start = reader.GetInt32(3), length = reader.GetInt32(4);
            texts[id] = RecordTexts.Of(() => new([reason, QuoteOf(text, start, length)], []));
        }
        return texts;
    }

    // A record with no summary row is absent. A record with no source row keeps an empty source.
    private static Dictionary<string, RecordTexts> Combine(Dictionary<string, List<string?[]>> summaries, Dictionary<string, List<string?[]>> sources,
        Func<string?[], string[], RecordTexts> create) =>
        summaries.ToDictionary(item => item.Key, item => RecordTexts.Of(() => create(item.Value[0], [.. sources.GetValueOrDefault(item.Key, []).Select(row => Text(row))])));

    private static string Text(string?[] row) => row[0] ?? throw new InvalidDataException("A cited source has no stored text.");

    private static string QuoteOf(string text, int start, int length) =>
        PublishedDocumentVersion.QuoteAt(text, start, length) ?? throw new InvalidDataException("A quotation does not match its offsets.");

    private async Task<Dictionary<string, List<(int Start, int Length)>>> SpansAsync(RevisionKeys keys, CancellationToken cancellationToken)
    {
        var spans = new Dictionary<string, List<(int, int)>>();
        await using var command = keys.Command(snapshot, StatementQuotations);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (!spans.TryGetValue(id, out var list)) spans[id] = list = [];
            list.Add((reader.GetInt32(1), reader.GetInt32(2)));
        }
        return spans;
    }

    // Each row holds the text columns after the record ID, in query order. Rows keep the order of the query.
    private async Task<Dictionary<string, List<string?[]>>> RowsAsync(RevisionKeys keys, string sql, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, List<string?[]>>();
        await using var command = keys.Command(snapshot, sql);
        command.Parameters.AddWithValue("limit", IndexedRecord.SourceLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var row = new string?[reader.FieldCount - 1];
            for (var column = 1; column < reader.FieldCount; column++) row[column - 1] = reader.IsDBNull(column) ? null : reader.GetString(column);
            if (!rows.TryGetValue(id, out var list)) rows[id] = list = [];
            list.Add(row);
        }
        return rows;
    }
}
