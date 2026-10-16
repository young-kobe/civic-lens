using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
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

    public Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult<ActionDraftEvidence?>(Evidence);

    public Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();

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
