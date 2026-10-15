using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;

namespace CivicLens.Application.Actions;

public interface IActionDraftStore : IAnalysisQueueStore<ActionDraftRun, ActionDraftView, ActionDraftListItem>
{
    Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);

    Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken);
}
