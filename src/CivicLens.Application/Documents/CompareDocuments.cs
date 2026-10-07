using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed class CompareDocuments(IDocumentExtractionStore extractions, IDocumentComparisonStore comparisons)
{
    public async Task<DocumentComparison> ExecuteAsync(string beforeExtractionId, string afterExtractionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beforeExtractionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterExtractionId);
        cancellationToken.ThrowIfCancellationRequested();
        var before = await extractions.GetAsync(beforeExtractionId, cancellationToken)
            ?? throw new ArgumentException("Before extraction was not found.", nameof(beforeExtractionId));
        var after = await extractions.GetAsync(afterExtractionId, cancellationToken)
            ?? throw new ArgumentException("After extraction was not found.", nameof(afterExtractionId));
        var comparison = DocumentComparison.Create(before, after, cancellationToken);
        return await comparisons.SaveAsync(comparison, cancellationToken);
    }
}
