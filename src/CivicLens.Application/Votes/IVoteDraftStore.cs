using CivicLens.Application.Analysis;

namespace CivicLens.Application.Votes;

public interface IVoteDraftStore : IAnalysisQueueStore<VoteDraftRun, VoteDraftView, VoteDraftListItem>, IStaleAnalysisRequeue
{
    Task<VoteDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);

    /// <summary>The candidate of a roll call that no other candidate supersedes, or null when the roll call has none.</summary>
    Task<string?> NewestCandidateIdAsync(VoteMeasureRoll roll, CancellationToken cancellationToken);

    Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken);

    /// <summary>Requeues the candidates that the worker refused while their record was rejected.</summary>
    Task<int> RequeueRefusedAsync(CancellationToken cancellationToken);

    Task<int> CountWaitingForBillAsync(CancellationToken cancellationToken);
}
