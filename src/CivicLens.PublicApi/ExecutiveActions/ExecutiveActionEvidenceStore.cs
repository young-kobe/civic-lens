using System.Text.Json;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Reads the cited evidence of one executive action revision from <c>public_api.executive_action_facts</c>, <c>public_api.executive_action_sources</c>,
/// <c>public_api.executive_action_summary_sentences</c>, and <c>public_api.executive_action_summary_sources</c>. Only the server page reads it, so the detail
/// API keeps its shape. Each quote comes from the saved source text at its span, as the vote store reads its citations.</summary>
public sealed class ExecutiveActionEvidenceStore
{
    private const string Sql = """
        SELECT (SELECT json_agg(json_build_object('position', f."position", 'id', f.extraction_id, 'start', f.citation_start, 'length', f.citation_length)
                         ORDER BY f."position")
                  FROM (SELECT f.* FROM public_api.executive_action_facts f
                         WHERE f.record_id = @id AND f.revision_number = @revision ORDER BY f."position" LIMIT @fact_limit) f),
               (SELECT json_agg(json_build_object('position', s."position", 'id', s.extraction_id, 'url', s.url, 'observedAt', s.observed_at_utc_ticks,
                           'text', CASE WHEN length(s.text) <= @text_limit THEN s.text END) ORDER BY s."position")
                  FROM (SELECT s.* FROM public_api.executive_action_sources s
                         WHERE s.record_id = @id AND s.revision_number = @revision ORDER BY s."position" LIMIT @source_limit) s),
               (SELECT json_agg(json_build_object('position', q."position", 'id', q.extraction_id, 'start', q.quote_start, 'length', q.quote_length)
                         ORDER BY q."position")
                  FROM public_api.executive_action_summary_sentences q WHERE q.record_id = @id AND q.revision_number = @revision),
               (SELECT json_agg(json_build_object('position', 0, 'id', b.extraction_id, 'url', b.url, 'observedAt', b.observed_at_utc_ticks,
                           'text', CASE WHEN length(b.text) <= @text_limit THEN b.text END))
                  FROM (SELECT b.* FROM public_api.executive_action_summary_sources b
                         WHERE b.record_id = @id AND b.revision_number = @revision LIMIT 2) b)
        """;

    /// <summary>Reads the evidence inside the transaction of the caller, so that it shares a snapshot with the record that the same transaction read.</summary>
    public async Task<ExecutiveActionEvidence> ReadAsync(NpgsqlTransaction transaction, string recordId, int revisionNumber, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("revision", revisionNumber);
        command.Parameters.AddWithValue("fact_limit", PublishedExecutiveAction.MaximumFacts + 1);
        command.Parameters.AddWithValue("source_limit", PublishedExecutiveAction.MaximumSources + 1);
        command.Parameters.AddWithValue("text_limit", PublishedDocumentVersion.MaximumTextLength);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var sources = Sources(Rows<SourceRow>(reader, 1), PublishedExecutiveAction.MaximumSources);
        var facts = Positioned(Rows<SpanRow>(reader, 0), PublishedExecutiveAction.MaximumFacts, "facts").Select(row => Fact(Citation(sources, row))).ToArray();
        if (facts.Length == 0) throw new InvalidDataException("A published executive action has no cited facts.");
        var quoteSource = Sources(Rows<SourceRow>(reader, 3), 1).SingleOrDefault();
        var quotes = Positioned(Rows<SpanRow>(reader, 2), PublishedExecutiveSummary.MaximumSentences, "summary quotes")
            .Select(row => Citation(quoteSource is null ? [] : [quoteSource], row)).ToArray();
        return new(facts, sources, quotes, quoteSource);
    }

    /// <summary>A fact citation quotes the line "Label: Value", as <see cref="PublishedExecutiveAction"/> validates. A label never holds ": ".</summary>
    private static PublishedExecutiveActionFact Fact(PublishedCitation citation)
    {
        var split = citation.Quote.IndexOf(": ", StringComparison.Ordinal);
        if (split < 1) throw new InvalidDataException("A published executive action fact does not quote a labeled line.");
        return new() { Label = citation.Quote[..split], Value = citation.Quote[(split + 2)..], Citation = citation };
    }

    private static PublishedCitation Citation(PublishedDocumentVersion[] sources, SpanRow row)
    {
        var source = sources.FirstOrDefault(item => item.ExtractionId == row.Id)
            ?? throw new InvalidDataException("A published executive action citation names no source of the record.");
        return new()
        {
            ExtractionId = row.Id,
            Start = row.Start,
            Length = row.Length,
            Quote = PublishedDocumentVersion.QuoteAt(source.Text, row.Start, row.Length)
                ?? throw new InvalidDataException("A published executive action citation does not match its source text.")
        };
    }

    private static PublishedDocumentVersion[] Sources(SourceRow[] rows, int maximum)
    {
        if (rows.Length > maximum) throw new InvalidDataException($"A published executive action has more than {maximum} sources here.");
        return [.. rows.Select(row => row is { Id: not null, Url: not null, ObservedAt: not null, Text: not null }
            ? new PublishedDocumentVersion { ExtractionId = row.Id, Url = row.Url, ObservedAtUtc = new(row.ObservedAt.Value, TimeSpan.Zero), Text = row.Text }
            : throw new InvalidDataException("A published executive action source has no metadata or no bounded text."))];
    }

    private static SpanRow[] Positioned(SpanRow[] rows, int maximum, string noun) =>
        rows.Length <= maximum && rows.Select(row => row.Position).SequenceEqual(Enumerable.Range(0, rows.Length))
            ? rows
            : throw new InvalidDataException($"Published executive action {noun} are misordered or exceed their limit.");

    private static T[] Rows<T>(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? []
        : JsonSerializer.Deserialize<T[]>(reader.GetString(ordinal), PublicationProtocol.JsonOptions)
          ?? throw new InvalidDataException("An executive action evidence view returned invalid content.");

    private sealed record SpanRow(int Position, string Id, int Start, int Length);
    private sealed record SourceRow(int Position, string? Id, string? Url, long? ObservedAt, string? Text);
}
