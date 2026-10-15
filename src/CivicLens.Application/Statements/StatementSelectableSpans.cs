using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public static class StatementSelectableSpans
{
    public const int Maximum = 200;

    public static ImmutableArray<StatementSelectableSpan> Build(DocumentExtraction extraction, StatementDraftRevision? saved)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        var options = new List<StatementSelectableSpan>();
        var seen = new HashSet<(int Start, int Length)>();
        void Add(SourceSpan span, StatementSpanOrigin origin)
        {
            if (options.Count < Maximum && span.Length <= StatementDraftRevision.MaximumQuotationLength && seen.Add((span.Start, span.Length)))
                options.Add(new(span, extraction.Text.Substring(span.Start, span.Length), origin));
        }

        foreach (var span in SavedSpans(saved)) Add(span, StatementSpanOrigin.Saved);
        foreach (var span in new QuotedText(extraction.Text).Passages(extraction.ExtractionId)) Add(span, StatementSpanOrigin.QuotedPassage);
        foreach (var span in Paragraphs(extraction)) Add(span, StatementSpanOrigin.Paragraph);
        return [.. options];
    }

    private static IEnumerable<SourceSpan> SavedSpans(StatementDraftRevision? saved) =>
        saved is null ? [] : saved.Quotations.Select(quotation => quotation.Span)
            .Concat(saved.StatementDateEvidence is { } evidence ? [evidence] : []);

    private static IEnumerable<SourceSpan> Paragraphs(DocumentExtraction extraction) =>
        DocumentLines.Split(extraction.Text, Maximum).Select(line => (line.Start, Text: line.Text.TrimEnd('\n')))
            .Where(line => line.Text.Trim().Length > 0)
            .Select(line => new SourceSpan(extraction.ExtractionId, line.Start, line.Text.Length));
}
