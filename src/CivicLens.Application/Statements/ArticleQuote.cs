using CivicLens.Core.Documents;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Statements;

internal static class ArticleQuote
{
    public static DocumentTextSpan? Locate(DocumentExtraction extraction, string? quote, string label, int maximumLength, List<string> errors,
        List<string> citationErrors) =>
        LocateStart(extraction.Text, quote, label, maximumLength, errors, citationErrors) is { } start
            ? new DocumentTextSpan(extraction, start, quote!.Length)
            : null;

    public static int? LocateStart(string articleText, string? quote, string label, int maximumLength, List<string> errors,
        List<string> citationErrors)
    {
        if (string.IsNullOrEmpty(quote) || quote.Length > maximumLength)
        {
            AddCitationError($"The quote for \"{label}\" needs 1 to {maximumLength} characters.", errors, citationErrors);
            return null;
        }
        var problem = UniqueQuote.Find(articleText, quote, out var start) switch
        {
            UniqueQuoteResult.Missing => "does not occur in the article text.",
            UniqueQuoteResult.Ambiguous => "occurs more than once in the article text. Quote a longer, unique passage.",
            UniqueQuoteResult.SplitsCharacter => "splits a character. Quote whole characters.",
            _ => null
        };
        if (problem is null) return start;
        AddCitationError($"The quote \"{Shorten(quote)}\" for \"{label}\" {problem}", errors, citationErrors);
        return null;
    }
}
