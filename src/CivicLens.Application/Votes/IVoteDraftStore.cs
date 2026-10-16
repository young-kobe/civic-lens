using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public interface IVoteDraftStore : IAnalysisQueue<VoteDraftRun>
{
    Task<VoteDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken);

    Task<VoteDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
}
