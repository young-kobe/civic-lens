using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementAnalysisStore : IAnalysisQueue<StatementGateRun>
{
    Task<StatementGateRun?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
    Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatementGateListItem>> ListAsync(StatementGateView? view, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<StatementGateView, int>> CountAsync(CancellationToken cancellationToken);
}
