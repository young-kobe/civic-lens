using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
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

internal sealed class FakeActionDraftModel : IActionDraftModel, IVoteDraftModel
{
    private readonly Queue<Func<AnalysisModelRequest, Task<AnalysisModelResponse>>> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeActionDraftModel Draft(string outputJson) =>
        Reply(_ => Task.FromResult(new AnalysisModelResponse("end_turn", outputJson, new(900, 100, 0, 0), "req_fixture")));

    public FakeActionDraftModel Reply(Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply)
    {
        lock (gate) replies.Enqueue(reply);
        return this;
    }

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
    {
        Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply;
        lock (gate)
        {
            requests.Add(request);
            if (!replies.TryDequeue(out reply!)) throw new InvalidOperationException("The fake model has no scripted reply.");
        }
        return reply(request);
    }
}
