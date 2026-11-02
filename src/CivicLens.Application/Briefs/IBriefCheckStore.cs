using CivicLens.Application.Analysis;

namespace CivicLens.Application.Briefs;

public interface IBriefCheckStore : IAnalysisQueue<BriefCheckRun>
{
    Task<BriefCheckEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken);

    Task<BriefCheckRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
}
