using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection.Bills;
using CivicLens.Core.Actions;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Actions;

internal sealed class FakeActionDraftStore : FakeAnalysisQueue<ActionDraftRun>, IActionDraftStore
{
    public FakeActionDraftStore(ActionDraftEvidence evidence)
    {
        Evidence = evidence;
        Items.Add(new(evidence.Candidate.CandidateId, ActionDraftTask.Task, State.Pending, null, 0, 0, null, null));
    }

    public ActionDraftEvidence Evidence { get; }
    public List<ActionDraftRevision> Revisions { get; } = [];

    public Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, bool withBillText, CancellationToken cancellationToken) =>
        Task.FromResult<ActionDraftEvidence?>(withBillText ? Evidence : Evidence with { BillText = null });

    public Task<string?> NewestCandidateIdAsync(CongressBillListItem bill, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(bill.Congress == Evidence.Candidate.Congress && bill.Type == Evidence.Candidate.BillType &&
            bill.Number == Evidence.Candidate.BillNumber ? Evidence.Candidate.CandidateId : null);

    public Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> RequeueStaleAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> RequeueRefusedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ActionDraftListItem>> ListAsync(ActionDraftView? view, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ActionDraftListItem>>([]);

    public Task<IReadOnlyDictionary<ActionDraftView, int>> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ActionDraftView, int>>(new Dictionary<ActionDraftView, int>());

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        ActionDraftLifecycle.CanTransition(record, checkpoint);

    protected override CivicLens.Core.Analysis.AnalysisRun RunOf(ActionDraftRun run) => run.Run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is ActionDraftCheckpoint { Revision: { } revision }) Revisions.Add(revision);
    }
}
