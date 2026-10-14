using CivicLens.Application.Statements;

namespace CivicLens.Application.Actions;

public interface IActionDraftStore : IStatementQueueStore<ActionDraftRun, ActionDraftView, ActionDraftListItem>
{
    Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);
}
