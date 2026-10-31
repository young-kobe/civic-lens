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
