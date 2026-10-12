namespace CivicLens.Application.Statements;

public interface IStatementAnalysisStore : IStatementQueueStore<StatementGateRun, StatementGateView, StatementGateListItem>
{
    Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
    Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken);
}
