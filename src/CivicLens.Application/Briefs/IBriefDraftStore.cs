using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public interface IBriefDraftStore : IAnalysisQueue<BriefDraftRun>
{
    Task<BriefDraftEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken);

    Task<BriefDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task EnqueueClosedWeeksAsync(CancellationToken cancellationToken);

    Task<int> RedraftAsync(DateOnly weekStart, CancellationToken cancellationToken);
}
