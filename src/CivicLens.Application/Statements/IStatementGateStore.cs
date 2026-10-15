using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementGateStore : IAnalysisQueueStore<StatementGateRun, StatementGateView, StatementGateListItem>
{
    Task<StatementArticle?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
    Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken);
}
