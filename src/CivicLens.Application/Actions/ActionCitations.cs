using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

internal static class ActionCitations
{
    public static SourceSpan TitleAndIntroduced(string extractionId, ImmutableArray<DocumentLine> detailLines)
    {
        var title = LineCitations.Line(extractionId, detailLines, "Title: ")!;
        var introduced = LineCitations.Line(extractionId, detailLines, "Introduced: ")!;
        return new(extractionId, title.Start, introduced.Start + introduced.Length - title.Start);
    }

    /// <summary>The span of an exact quote in the body of the bill text. It is null when the body does not hold the quote.</summary>
    public static SourceSpan? BillTextQuote(ActionDraftBillText billText, string quote)
    {
        if (quote.Length == 0) return null;
        var start = billText.Text.IndexOf(quote, billText.BodyStart, StringComparison.Ordinal);
        return start < 0 || SplitsSurrogatePair(billText.Text, start) || SplitsSurrogatePair(billText.Text, start + quote.Length)
            ? null : new(billText.ExtractionId, start, quote.Length);
    }

    private static bool SplitsSurrogatePair(string text, int boundary) => boundary > 0 && boundary < text.Length &&
        char.IsHighSurrogate(text[boundary - 1]) && char.IsLowSurrogate(text[boundary]);

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
}
