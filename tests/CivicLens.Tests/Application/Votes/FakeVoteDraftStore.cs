using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Votes;

internal sealed class FakeVoteDraftStore : FakeAnalysisQueue<VoteDraftRun>, IVoteDraftStore
{
    public FakeVoteDraftStore(VoteDraftEvidence evidence)
    {
        Evidence = evidence;
        Items.Add(new(evidence.Candidate.CandidateId, VoteDraftTask.Task, State.Pending, null, 0, 0, null, null));
    }

    public VoteDraftEvidence Evidence { get; }
    public List<VoteDraftRevision> Revisions { get; } = [];

    public Task<VoteDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult<VoteDraftEvidence?>(Evidence);

    public Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> RequeueOlderPromptAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> CountWaitingForBillAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyList<VoteDraftListItem>> ListAsync(VoteDraftView? view, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VoteDraftListItem>>([]);

    public Task<IReadOnlyDictionary<VoteDraftView, int>> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<VoteDraftView, int>>(new Dictionary<VoteDraftView, int>());

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        VoteDraftLifecycle.CanTransition(record, checkpoint);

    protected override CivicLens.Core.Analysis.AnalysisRun RunOf(VoteDraftRun run) => run.Run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is VoteDraftCheckpoint { Revision: { } revision }) Revisions.Add(revision);
    }
}
