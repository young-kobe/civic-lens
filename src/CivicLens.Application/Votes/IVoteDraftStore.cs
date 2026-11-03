using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public interface IVoteDraftStore : IAnalysisQueueStore<VoteDraftRun, VoteDraftView, VoteDraftListItem>
{
    Task<VoteDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);

    Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken);

    Task<int> RequeueOlderPromptAsync(CancellationToken cancellationToken);

    /// <summary>Requeues the candidates that the worker refused while their record was rejected.</summary>
    Task<int> RequeueRefusedAsync(CancellationToken cancellationToken);

    Task<int> CountWaitingForBillAsync(CancellationToken cancellationToken);
}
