using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Application.Collection;

namespace CivicLens.Application.Documents;

public sealed class ExtractDocument(ICollectionAttemptStore attempts, IDocumentTextExtractor extractor,
    IDocumentExtractionStore extractions)
{
    public async Task<DocumentExtraction> ExecuteAsync(string attemptId, string artifactRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await attempts.GetAsync(attemptId, cancellationToken);
        if (stored?.AttemptResult is not CapturedAttemptResult captured)
            throw new ArgumentException("Attempt must be an imported captured result.", nameof(attemptId));

        var text = await extractor.ExtractAsync(captured, artifactRoot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var extraction = new DocumentExtraction(captured, extractor.ParserVersion, extractor.NormalizationVersion, text);
        return await extractions.SaveAsync(extraction, cancellationToken);
    }
}
