using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

internal static class ActionCitations
{
    public static SourceSpan? Line(ActionDraftPage page, ImmutableArray<DocumentLine> lines, string prefix)
    {
        var line = lines.FirstOrDefault(item => item.Text.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? null : Span(page, line);
    }

    public static IReadOnlyDictionary<string, SourceSpan> ExactLines(ActionDraftPage page) =>
        DocumentLines.Split(page.Text).DistinctBy(Content).ToDictionary(Content, line => Span(page, line), StringComparer.Ordinal);

    public static SourceSpan? SummaryBlock(ActionDraftPage page, CongressBillSummary summary)
    {
        var version = CongressBillRenderer.SummaryVersionLine(summary);
        var updated = CongressBillRenderer.SummaryUpdatedLine(summary);
        var start = 0;
        while (start <= page.Text.Length)
        {
            var end = page.Text.IndexOf("\n\n", start, StringComparison.Ordinal);
            if (end < 0) end = page.Text.Length;
            var lines = page.Text[start..end].Split('\n');
            if (lines[0] == version && lines.Contains(updated)) return new(page.ExtractionId, start, end - start);
            start = end + 2;
        }
        return null;
    }

    private static SourceSpan Span(ActionDraftPage page, DocumentLine line) => new(page.ExtractionId, line.Start, Content(line).Length);

    private static string Content(DocumentLine line) => line.Text.TrimEnd('\n');
}
