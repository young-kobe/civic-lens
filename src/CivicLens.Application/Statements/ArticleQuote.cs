using CivicLens.Core.Documents;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Statements;

internal static class ArticleQuote
{
    public static DocumentTextSpan? Locate(DocumentExtraction extraction, string? quote, string label, int maximumLength, List<string> errors)
    {
        if (string.IsNullOrEmpty(quote) || quote.Length > maximumLength)
        {
            errors.Add($"The quote for \"{label}\" needs 1 to {maximumLength} characters.");
            return null;
        }
        var problem = UniqueQuote.Find(extraction.Text, quote, out var start) switch
        {
            UniqueQuoteResult.Missing => "does not occur in the article text.",
            UniqueQuoteResult.Ambiguous => "occurs more than once in the article text. Quote a longer, unique passage.",
            UniqueQuoteResult.SplitsCharacter => "splits a character. Quote whole characters.",
            _ => null
        };
        if (problem is null) return new DocumentTextSpan(extraction, start, quote.Length);
        errors.Add($"The quote \"{Shorten(quote)}\" for \"{label}\" {problem}");
        return null;
    }
}
