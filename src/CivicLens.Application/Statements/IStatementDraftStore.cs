using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementDraftStore : IAnalysisQueue<StatementDraftRun>
{
    Task<StatementDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<StatementDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementDraftListItem>> ListAsync(StatementDraftView? view, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<StatementDraftView, int>> CountAsync(CancellationToken cancellationToken);
}
