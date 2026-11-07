using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

/// <summary>The one owner of the checks of a bill line and its supporting quote span.</summary>
internal static class ActionBillLineRule
{
    public static (string Line, SourceSpan Citation)? Check(AnalysisOutputLine? output, CongressBillSummaryText? summary, SourceSpan? summaryTextSpan)
    {
        var line = output?.Line?.Trim() ?? string.Empty;
        var quote = output?.Quote ?? string.Empty;
        if (summary is null || summaryTextSpan is null || line.Length is 0 or > ActionDraftRevision.MaximumBillLineLength ||
            ActionTextCitation.WordCount(quote) < ActionDraftRevision.MinimumTextQuoteWords ||
            ReadingGrade.Rounded(line) > ReadingGrade.MaximumGrade)
            return null;
        var start = summary.Text.IndexOf(quote, StringComparison.Ordinal);
        if (start < 0 || summaryTextSpan.Length != summary.Text.Length) return null;
        return (line, new SourceSpan(summaryTextSpan.ExtractionId, summaryTextSpan.Start + start, quote.Length));
    }
}
