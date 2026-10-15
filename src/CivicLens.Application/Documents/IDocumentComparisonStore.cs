using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public interface IDocumentComparisonStore
{
    Task<DocumentComparison?> GetAsync(string comparisonId, CancellationToken cancellationToken);
    Task<DocumentComparison> CompareAndSaveAsync(string beforeExtractionId, string afterExtractionId,
        CancellationToken cancellationToken);
}
