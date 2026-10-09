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
        return await ExecuteAsync(before, after, cancellationToken);
    }

    internal async Task<DocumentComparison> ExecuteAsync(DocumentExtraction before, DocumentExtraction after,
        CancellationToken cancellationToken)
    {
        var comparison = await CompareAsync(before, after, cancellationToken);
        return await SaveAsync(comparison, cancellationToken);
    }

    internal Task<DocumentComparison> CompareAsync(DocumentExtraction before, DocumentExtraction after,
        CancellationToken cancellationToken) => Task.FromResult(DocumentComparison.Create(before, after, cancellationToken));

    internal Task<DocumentComparison> SaveAsync(DocumentComparison comparison, CancellationToken cancellationToken) =>
        comparisons.SaveAsync(comparison, cancellationToken);
}
