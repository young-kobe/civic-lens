using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Tests.Application.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Relationships;

internal sealed class FakeRelationshipDraftStore : FakeAnalysisQueue<RelationshipDraftRun>, IRelationshipDraftStore
{
    public FakeRelationshipDraftStore(RelationshipDraftEvidence? evidence)
    {
        Evidence = evidence;
        Items.Add(new(evidence?.PairId ?? new string('f', 64), RelationshipDraftTask.Task, State.Pending, null, 0, 0, null, null));
    }

    public RelationshipDraftEvidence? Evidence { get; }
    public List<RelationshipRevision> Revisions { get; } = [];

    public Task<RelationshipDraftEvidence?> ReadEvidenceAsync(string pairId, CancellationToken cancellationToken) =>
        Task.FromResult(Evidence);

    public Task<int> RefreshPairsAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<int> CountStatementsWithoutCongressAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyList<RelationshipDraftListItem>> ListAsync(RelationshipDraftView? view, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RelationshipDraftListItem>>([]);

    public Task<IReadOnlyDictionary<RelationshipDraftView, int>> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<RelationshipDraftView, int>>(new Dictionary<RelationshipDraftView, int>());

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        RelationshipDraftLifecycle.CanTransition(record, checkpoint);

    protected override CivicLens.Core.Analysis.AnalysisRun RunOf(RelationshipDraftRun run) => run.Run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is RelationshipDraftCheckpoint { Revision: { } revision }) Revisions.Add(revision);
    }
}

internal sealed class FakeRelationshipDraftModel : IRelationshipDraftModel
{
    private readonly Queue<string> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeRelationshipDraftModel Draft(string outputJson)
    {
        lock (gate) replies.Enqueue(outputJson);
        return this;
    }

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
    {
        string reply;
        lock (gate)
        {
            requests.Add(request);
            if (!replies.TryDequeue(out reply!)) throw new InvalidOperationException("The fake model has no scripted reply.");
        }
        return Task.FromResult(new AnalysisModelResponse("end_turn", reply, new(900, 100, 0, 0), "req_fixture"));
    }
}
