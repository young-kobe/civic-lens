namespace CivicLens.Application.Documents;

public interface IDocumentComparisonBackfillStore
{
    Task<DocumentComparisonBackfillBatch> BackfillBatchAsync(string? afterComparisonId, int size,
        CancellationToken cancellationToken);
}
