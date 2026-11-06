using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Application.Actions;

public interface IActionDraftStore : IAnalysisQueueStore<ActionDraftRun, ActionDraftView, ActionDraftListItem>, IStaleAnalysisRequeue
{
    /// <summary>Reads the evidence of a candidate. The bill text is read only when <paramref name="withBillText"/> is true.</summary>
    Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, bool withBillText, CancellationToken cancellationToken);

    /// <summary>Finds the newest candidate of a bill, or null when the bill has none.</summary>
    Task<string?> NewestCandidateIdAsync(CongressBillListItem bill, CancellationToken cancellationToken);

    Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken);

    /// <summary>Requeues the candidates that the worker refused while their record was rejected.</summary>
    Task<int> RequeueRefusedAsync(CancellationToken cancellationToken);
}
