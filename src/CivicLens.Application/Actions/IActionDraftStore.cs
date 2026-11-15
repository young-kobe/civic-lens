using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;

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

    /// <summary>Published records whose current revision holds retired text, by record ID after <paramref name="afterRecordId"/>, at most
    /// <paramref name="limit"/>. Retired text is model text with no bill text package, or a code headline that reads "introduced".</summary>
    Task<IReadOnlyList<ActionCodeTextTarget>> ListPublishedRetiredTextAsync(int limit, string? afterRecordId, CancellationToken cancellationToken);

    /// <summary>Appends a code revision and its Bypassed run outside the worker, with the queue entry settled. Returns null when it appended, else the reason.</summary>
    Task<string?> AppendCodeTextAsync(ActionDraftRevision revision, ActionDraftRun run, int readStateVersion, CancellationToken cancellationToken);
}
