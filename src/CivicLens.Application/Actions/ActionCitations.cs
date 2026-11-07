using System.Collections.Immutable;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Actions;

internal static class ActionCitations
{
    public const string TitlePrefix = "Title: ";

    public static SourceSpan TitleAndIntroduced(string extractionId, ImmutableArray<DocumentLine> detailLines)
    {
        var title = LineCitations.Line(extractionId, detailLines, TitlePrefix)!;
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

    public static SourceSpan? SummaryText(ActionDraftPage page, CongressBillSummaryText summary)
    {
        if (summary.Text.Length == 0) return null;
        var block = SummaryBlock(page, summary.Summary) ?? throw new InvalidDataException("The selected CRS summary has no source span.");
        var start = block.Start + block.Length - summary.Text.Length;
        if (start < block.Start || !page.Text.AsSpan(start, summary.Text.Length).SequenceEqual(summary.Text))
            throw new InvalidDataException("The selected CRS summary text is not the suffix of its source block.");
        return new(page.ExtractionId, start, summary.Text.Length);
    }
}
