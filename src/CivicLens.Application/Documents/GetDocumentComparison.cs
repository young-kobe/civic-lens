using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public sealed class GetDocumentComparison(IDocumentComparisonStore comparisons)
{
    public Task<DocumentComparison?> ExecuteAsync(string comparisonId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comparisonId);
        cancellationToken.ThrowIfCancellationRequested();
        return comparisons.GetAsync(comparisonId, cancellationToken);
    }
}
