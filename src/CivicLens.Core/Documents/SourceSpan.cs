namespace CivicLens.Core.Documents;

public sealed record SourceSpan(string ExtractionId, int Start, int Length)
{
    public bool Covers(SourceSpan inner) =>
        ExtractionId == inner.ExtractionId && Start <= inner.Start && inner.Start + inner.Length <= Start + Length;

    public string QuoteIn(IReadOnlyDictionary<string, DocumentExtraction> extractions) =>
        new DocumentTextSpan(extractions[ExtractionId], Start, Length).Quote;
}
