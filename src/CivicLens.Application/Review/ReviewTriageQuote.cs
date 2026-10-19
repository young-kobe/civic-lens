using CivicLens.Core.Documents;

namespace CivicLens.Application.Review;

public sealed record ReviewTriageQuote(string Label, string Text)
{
    public static ReviewTriageQuote Of(string label, SourceSpan span, IReadOnlyDictionary<string, DocumentExtraction> extractions) =>
        new(label, span.QuoteIn(extractions));
}
