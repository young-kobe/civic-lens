using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed class GetDocumentCitation(IDocumentExtractionStore extractions)
{
    public async Task<DocumentTextSpan> ExecuteAsync(string extractionId, int start, int length,
        CancellationToken cancellationToken = default)
    {
        var extraction = await extractions.GetAsync(extractionId, cancellationToken)
            ?? throw new ArgumentException("Extraction was not found.", nameof(extractionId));
        return new DocumentTextSpan(extraction, start, length);
    }
}
