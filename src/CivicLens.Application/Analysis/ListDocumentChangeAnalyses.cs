using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class ListDocumentChangeAnalyses(IDocumentChangeAnalysisStore store)
{
    public async Task<IReadOnlyDictionary<string, DocumentChangeAnalysisRecord>> ExecuteAsync(ReviewActor actor,
        IReadOnlyCollection<string> comparisonIds, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(comparisonIds);
        if (comparisonIds.Count > DocumentChangeAnalysisPolicy.MaximumBatch) throw new ArgumentOutOfRangeException(nameof(comparisonIds));
        if (comparisonIds.Count == 0) return new Dictionary<string, DocumentChangeAnalysisRecord>();
        foreach (var id in comparisonIds) ReviewValidation.ValidateHash(id, nameof(comparisonIds));
        var records = await store.GetByComparisonIdsAsync(comparisonIds, cancellationToken);
        return records.ToDictionary(record => record.ComparisonId, StringComparer.Ordinal);
    }
}
