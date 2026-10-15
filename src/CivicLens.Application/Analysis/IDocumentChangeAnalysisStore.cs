using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public interface IDocumentChangeAnalysisStore : IAnalysisQueue<AnalysisRun>
{
    Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(IReadOnlyCollection<string> comparisonIds,
        CancellationToken cancellationToken);
    Task<AnalysisRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken);
    Task<DocumentChangeDraftEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken);
}
