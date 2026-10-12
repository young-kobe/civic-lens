namespace CivicLens.Application.Statements;

public interface IStatementDraftStore : IStatementQueueStore<StatementDraftRun, StatementDraftView, StatementDraftListItem>
{
    Task<StatementDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
}
