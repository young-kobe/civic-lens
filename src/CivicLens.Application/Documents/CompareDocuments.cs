using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed class CompareDocuments(IDocumentComparisonStore comparisons)
{
    public Task<DocumentComparison> ExecuteAsync(string beforeExtractionId, string afterExtractionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeExtractionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterExtractionId);
        cancellationToken.ThrowIfCancellationRequested();
        return comparisons.CompareAndSaveAsync(beforeExtractionId, afterExtractionId, cancellationToken);
    }
}
