using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

/// <summary>The one owner of the checks of a bill line. The quote is an exact span of the summary text that the model read. The line has at most
/// <see cref="ActionDraftRevision.MaximumBillLineLength"/> characters and reads at <see cref="ReadingGrade.MaximumGrade"/> or below.
/// A line that fails a check is dropped, so the bill has no line.</summary>
internal static class ActionBillLineRule
{
    public static string? Check(AnalysisOutputLine? output, CongressBillSummaryText? summary)
    {
        var line = output?.Line?.Trim() ?? string.Empty;
        var quote = output?.Quote ?? string.Empty;
        return summary is not null && line.Length > 0 && line.Length <= ActionDraftRevision.MaximumBillLineLength &&
            ActionTextCitation.WordCount(quote) >= ActionDraftRevision.MinimumTextQuoteWords &&
            summary.Text.Contains(quote, StringComparison.Ordinal) && ReadingGrade.Rounded(line) <= ReadingGrade.MaximumGrade
            ? line
            : null;
    }
}
