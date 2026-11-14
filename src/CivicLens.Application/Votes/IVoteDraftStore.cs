using CivicLens.Application.Analysis;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public interface IVoteDraftStore : IAnalysisQueueStore<VoteDraftRun, VoteDraftView, VoteDraftListItem>, IStaleAnalysisRequeue
{
    Task<VoteDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken);

    Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken);

    /// <summary>Requeues the candidates that the worker refused while their record was rejected.</summary>
    Task<int> RequeueRefusedAsync(CancellationToken cancellationToken);

    Task<int> CountWaitingForBillAsync(CancellationToken cancellationToken);

    /// <summary>Published votes whose current revision holds model text, by record ID after <paramref name="afterRecordId"/>, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<VoteCodeTextTarget>> ListPublishedModelTextAsync(int limit, string? afterRecordId, CancellationToken cancellationToken);

    /// <summary>Appends a code revision and its Bypassed run outside the worker, with the queue entry settled. Returns null when it appended, else the reason.</summary>
    Task<string?> AppendCodeTextAsync(VoteDraftRevision revision, VoteDraftRun run, CancellationToken cancellationToken);
}
