using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>Reads one active document change and its bounded evidence from the public views in one statement.</summary>
public sealed class DocumentChangeStore(NpgsqlDataSource views)
{
    private const int CitationLimit = PublishedDocumentChange.MaximumCitations + 1;
    private const int ChangeLimit = PublishedDocumentChange.MaximumChanges + 1;
    private const int WordEditLimit = PublishedDocumentChange.MaximumWordEditsPerChange + 1;
    private const int LabelLimit = PublishedRecordEntry.MaximumFilterValues + 1;

    private const string Sql = """
        SELECT a.release_number, d.record_id, d.revision_number, d.headline, d.summary, d.text_origin, d.label, d.significance, d.limits,
               d.institution, d.change_date, d.hunk_count, d.issue_ids[1 : @label_limit],
               COALESCE((SELECT json_agg(o.official_id ORDER BY o.position)
                           FROM (SELECT official_id, position FROM public_api.document_change_officials
                                  WHERE record_id = d.record_id AND revision_number = d.revision_number
                                  ORDER BY position LIMIT @label_limit) o), '[]'::json),
               CASE WHEN bounds.ready THEN
                   (SELECT json_agg(json_build_object('side', s.source_side, 'id', s.extraction_id, 'url', s.url,
                        'observedAt', s.observed_at_utc_ticks, 'text', s.text) ORDER BY s.source_side)
                      FROM (SELECT source_side, extraction_id, url, observed_at_utc_ticks,
                                   CASE WHEN length(text) <= @text_limit THEN text END AS text
                              FROM public_api.document_change_sources
                             WHERE record_id = d.record_id AND revision_number = d.revision_number
                             ORDER BY source_side LIMIT 3) s)
               END,
               CASE WHEN bounds.ready THEN
                   COALESCE((SELECT json_agg(json_build_object('position', h.position, 'beforeStart', h.before_start, 'beforeLength', h.before_length,
                        'afterStart', h.after_start, 'afterLength', h.after_length, 'beforeContextStart', h.before_context_start,
                        'beforeContextLength', h.before_context_length, 'afterContextStart', h.after_context_start,
                        'afterContextLength', h.after_context_length, 'wordEditCount', h.word_edit_count, 'wordEdits', h.word_edits)
                        ORDER BY h.position)
                      FROM (SELECT h.position, h.before_start, h.before_length, h.after_start, h.after_length,
                                   h.before_context_start, h.before_context_length, h.after_context_start, h.after_context_length,
                                   h.word_edit_count,
                                   CASE WHEN h.word_edit_count BETWEEN 0 AND @maximum_word_edits THEN
                                       COALESCE((SELECT json_agg(json_build_object('position', e.position, 'beforeStart', e.before_start, 'beforeLength', e.before_length,
                                            'afterStart', e.after_start, 'afterLength', e.after_length) ORDER BY e.position)
                                          FROM (SELECT position, before_start, before_length, after_start, after_length
                                                  FROM public_api.document_change_word_edits
                                                 WHERE record_id = d.record_id AND revision_number = d.revision_number AND hunk_position = h.position
                                                 ORDER BY position LIMIT @word_edit_limit) e), '[]'::json)
                                   END AS word_edits
                              FROM public_api.document_change_hunks h
                             WHERE h.record_id = d.record_id AND h.revision_number = d.revision_number
                             ORDER BY h.position LIMIT @change_limit) h), '[]'::json)
               END,
               CASE WHEN bounds.ready THEN
                   (SELECT json_agg(json_build_object('position', c.position, 'id', c.extraction_id, 'start', c.citation_start, 'length', c.citation_length,
                        'dateEvidence', c.is_change_date_evidence) ORDER BY c.position)
                      FROM (SELECT position, extraction_id, citation_start, citation_length, is_change_date_evidence
                              FROM public_api.document_change_citations
                             WHERE record_id = d.record_id AND revision_number = d.revision_number
                             ORDER BY position LIMIT @citation_limit) c)
               END
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.document_changes d ON d.record_id = @id
          CROSS JOIN LATERAL (SELECT d.hunk_count BETWEEN 1 AND @maximum_changes AS ready) bounds
        """;

    public Task<(int? Release, DocumentChangeDetail? Detail)> FindAsync(string recordId, CancellationToken cancellationToken) =>
        ReadDetailAsync(views.CreateCommand(Sql), recordId, cancellationToken);

    /// <summary>Reads the detail inside the transaction of the caller, so that it shares a snapshot with the other reads of that transaction.</summary>
    public Task<(int? Release, DocumentChangeDetail? Detail)> FindAsync(NpgsqlTransaction transaction, string recordId, CancellationToken cancellationToken) =>
        ReadDetailAsync(new NpgsqlCommand(Sql, transaction.Connection, transaction), recordId, cancellationToken);

    private static async Task<(int? Release, DocumentChangeDetail? Detail)> ReadDetailAsync(NpgsqlCommand command, string recordId, CancellationToken cancellationToken)
    {
        await using var _ = command;
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("citation_limit", CitationLimit);
        command.Parameters.AddWithValue("change_limit", ChangeLimit);
        command.Parameters.AddWithValue("word_edit_limit", WordEditLimit);
        command.Parameters.AddWithValue("maximum_changes", PublishedDocumentChange.MaximumChanges);
        command.Parameters.AddWithValue("maximum_word_edits", PublishedDocumentChange.MaximumWordEditsPerChange);
        command.Parameters.AddWithValue("label_limit", LabelLimit);
        command.Parameters.AddWithValue("text_limit", PublishedDocumentVersion.MaximumTextLength);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return (null, null);
        var release = reader.GetInt32(0);
        if (reader.IsDBNull(1)) return (release, null);

        if (reader.IsDBNull(11) || reader.GetInt32(11) is < 1 or > PublishedDocumentChange.MaximumChanges)
            throw new InvalidDataException("The published document change has no bounded hunk count.");
        var hunkCount = reader.GetInt32(11);

        var (before, after) = ReadSources(reader);
        var changes = ReadChanges(reader, hunkCount);
        var (citations, dateEvidence) = ReadCitations(reader, before, after);
        var row = new DocumentChangeRow
        {
            RecordId = reader.GetString(1),
            RevisionNumber = reader.GetInt32(2),
            Headline = reader.GetString(3),
            Summary = reader.GetString(4),
            TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 5),
            Label = ViewColumns.Text(reader, 6),
            Significance = ViewColumns.Text(reader, 7),
            Limits = ViewColumns.Text(reader, 8),
            Institution = reader.GetString(9),
            ChangeDate = ViewColumns.Date(reader, 10),
            OfficialIds = ReadJson<string[]>(reader, 13, "officials"),
            IssueIds = reader.GetFieldValue<string[]>(12)
        };
        if (row.OfficialIds.Length >= LabelLimit || row.IssueIds.Length >= LabelLimit)
            throw new InvalidDataException("Published document change exceeds its label limit.");
        ValidateRow(row);
        PublishedDocumentChange.ValidateContent(before, after, citations, changes, row.ChangeDate, dateEvidence);
        return (release, new(release, row, before, after, changes, citations, dateEvidence));
    }

    private static (PublishedDocumentVersion Before, PublishedDocumentVersion After) ReadSources(NpgsqlDataReader reader)
    {
        var before = default(PublishedDocumentVersion)!;
        var after = default(PublishedDocumentVersion)!;
        var sources = ReadJson<SourceRow[]>(reader, 14, "sources");
        if (sources.Length != 2 || sources.Count(source => source.Side == "Before") != 1 || sources.Count(source => source.Side == "After") != 1)
            throw new InvalidDataException("The active document change must have exactly one source for each side.");
        foreach (var row in sources)
        {
            if (row.Text is null || row.Text.Length > PublishedDocumentVersion.MaximumTextLength)
                throw new InvalidDataException("Published source text is missing or exceeds its maximum length.");
            var source = new PublishedDocumentVersion
            {
                ExtractionId = row.Id,
                Url = row.Url,
                ObservedAtUtc = ObservedAtUtc(row.ObservedAt),
                Text = row.Text
            };
            if (row.Side == "Before") before = source;
            else after = source;
        }
        return (before, after);
    }

    private static PublishedChange[] ReadChanges(NpgsqlDataReader reader, int hunkCount)
    {
        var rawChanges = ReadJson<ChangeRow[]>(reader, 15, "changes");
        if (rawChanges.Length > PublishedDocumentChange.MaximumChanges)
            throw new InvalidDataException("Published document change exceeds its change limit.");
        if (!rawChanges.Select(change => change.Position).SequenceEqual(Enumerable.Range(0, rawChanges.Length)))
            throw new InvalidDataException("Published document change hunk positions are incomplete or misordered.");
        if (hunkCount != rawChanges.Length)
            throw new InvalidDataException("Published document change hunk count does not match its details.");
        var changes = rawChanges.Select(change =>
        {
            if (change.WordEditCount is null || change.WordEdits is null || change.WordEditCount != change.WordEdits.Length ||
                !change.WordEdits.Select(edit => edit.Position).SequenceEqual(Enumerable.Range(0, change.WordEdits.Length)) ||
                change.WordEdits.Length > PublishedDocumentChange.MaximumWordEditsPerChange)
                throw new InvalidDataException("Published word edit count does not match its details.");
            return new PublishedChange
            {
                BeforeStart = change.BeforeStart,
                BeforeLength = change.BeforeLength,
                AfterStart = change.AfterStart,
                AfterLength = change.AfterLength,
                BeforeContextStart = change.BeforeContextStart,
                BeforeContextLength = change.BeforeContextLength,
                AfterContextStart = change.AfterContextStart,
                AfterContextLength = change.AfterContextLength,
                WordEdits = change.WordEdits.Select(edit => new PublishedWordEdit
                { BeforeStart = edit.BeforeStart, BeforeLength = edit.BeforeLength, AfterStart = edit.AfterStart, AfterLength = edit.AfterLength }).ToArray()
            };
        }).ToArray();
        return changes;
    }

    private static (PublishedCitation[] Citations, PublishedCitation? DateEvidence) ReadCitations(
        NpgsqlDataReader reader, PublishedDocumentVersion before, PublishedDocumentVersion after)
    {
        var citationRows = ReadJson<CitationRow[]>(reader, 16, "citations");
        if (citationRows.Length is 0 or > PublishedDocumentChange.MaximumCitations) throw new InvalidDataException("Published document change citation count is invalid.");
        if (!citationRows.Select(citation => citation.Position).SequenceEqual(Enumerable.Range(0, citationRows.Length)))
            throw new InvalidDataException("Published document change citation positions are incomplete or misordered.");
        var dateRows = citationRows.Where(row => row.DateEvidence).ToArray();
        if (dateRows.Length > 1) throw new InvalidDataException("Published document change has multiple date evidence citations.");
        PublishedCitation ConvertCitation(CitationRow row)
        {
            var text = row.Id == before.ExtractionId ? before.Text : row.Id == after.ExtractionId ? after.Text
                : throw new InvalidDataException("Published citation must cite one of the record's versions.");
            if (row.Start < 0 || row.Length <= 0 || row.Start > text.Length - row.Length)
                throw new InvalidDataException("Published citation quote does not match its offsets.");
            return new() { ExtractionId = row.Id, Start = row.Start, Length = row.Length, Quote = text.Substring(row.Start, row.Length) };
        }
        var citations = citationRows.Select(ConvertCitation).ToArray();
        var dateEvidence = dateRows.Length == 0 ? null : citations[dateRows[0].Position];
        return (citations, dateEvidence);
    }

    private static T ReadJson<T>(NpgsqlDataReader reader, int ordinal, string noun) => reader.IsDBNull(ordinal)
        ? throw new InvalidDataException($"The document change {noun} view returned no content.")
        : JsonSerializer.Deserialize<T>(reader.GetString(ordinal), PublicationProtocol.JsonOptions)
          ?? throw new InvalidDataException($"The document change {noun} view returned invalid content.");

    private static DateTimeOffset ObservedAtUtc(long ticks)
    {
        try { return new(ticks, TimeSpan.Zero); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("Published source observation time is invalid.", exception); }
    }

    private static void ValidateRow(DocumentChangeRow row)
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.DocumentChange, row.RecordId) || row.RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(row.Headline) || string.IsNullOrWhiteSpace(row.Summary) || string.IsNullOrWhiteSpace(row.Institution))
            throw new InvalidDataException("Published document change identity or wording is invalid.");
        if (!ValidLabels(row.OfficialIds) || !ValidLabels(row.IssueIds))
            throw new InvalidDataException("Published document change officials or issues are invalid.");
    }

    private static bool ValidLabels(string[] values) => values.All(value => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= PublishedRecordEntry.MaximumFilterValueLength) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;

    private sealed record SourceRow(string Side, string Id, string Url, long ObservedAt, string Text);
    private sealed record CitationRow(int Position, string Id, int Start, int Length, bool DateEvidence);
    private sealed record EditRow(int Position, int BeforeStart, int BeforeLength, int AfterStart, int AfterLength);
    private sealed record ChangeRow(int Position, int BeforeStart, int BeforeLength, int AfterStart, int AfterLength,
        int BeforeContextStart, int BeforeContextLength, int AfterContextStart, int AfterContextLength,
        int? WordEditCount, EditRow[]? WordEdits);
}
