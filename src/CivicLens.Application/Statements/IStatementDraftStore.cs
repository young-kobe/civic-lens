using CivicLens.Application.Analysis;

namespace CivicLens.Application.Statements;

public interface IStatementDraftStore : IAnalysisQueueStore<StatementDraftRun, StatementDraftView, StatementDraftListItem>
{
    Task<StatementDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
}
