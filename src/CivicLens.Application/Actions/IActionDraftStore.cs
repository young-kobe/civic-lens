using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

public interface IActionDraftStore : IAnalysisQueueStore<ActionDraftRun, ActionDraftView, ActionDraftListItem>
{
    /// <summary>Reads the evidence of a candidate. The bill text is read only when <paramref name="withBillText"/> is true.</summary>
    Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, bool withBillText, CancellationToken cancellationToken);

    Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken);

    Task<int> RequeueOlderPromptAsync(CancellationToken cancellationToken);

    /// <summary>Requeues the candidates that the worker refused while their record was rejected.</summary>
    Task<int> RequeueRefusedAsync(CancellationToken cancellationToken);
}
