using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementAnalysisStore : IAnalysisQueueStore<StatementGateRun, StatementGateView, StatementGateListItem>
{
    Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
    Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken);
}
