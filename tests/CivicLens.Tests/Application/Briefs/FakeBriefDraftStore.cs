using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Core.Briefs;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Briefs;

internal sealed class FakeBriefDraftStore : FakeAnalysisQueue<BriefDraftRun>, IBriefDraftStore
{
    public FakeBriefDraftStore(BriefDraftEvidence evidence)
    {
        Evidence = evidence;
        Items.Add(new(BriefFixtures.RecordId, BriefDraftTask.Task, State.Pending, null, 0, 0, null, null));
    }

    public BriefDraftEvidence Evidence { get; }
    public List<BriefRevision> Revisions { get; } = [];
    public List<DateOnly> Redrafted { get; } = [];

    public Task<BriefDraftEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken) =>
        Task.FromResult<BriefDraftEvidence?>(Evidence);

    public int EnqueueCalls { get; private set; }

    public Task EnqueueClosedWeeksAsync(CancellationToken cancellationToken)
    {
        EnqueueCalls++;
        return Task.CompletedTask;
    }

    public Task<int> RedraftAsync(DateOnly weekStart, CancellationToken cancellationToken)
    {
        Redrafted.Add(weekStart);
        return Task.FromResult(1);
    }

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        BriefDraftLifecycle.CanTransition(record, checkpoint);

    protected override CivicLens.Core.Analysis.AnalysisRun RunOf(BriefDraftRun run) => run.Run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is BriefDraftCheckpoint { Revision: { } revision }) Revisions.Add(revision);
    }
}

internal sealed class FakeBriefDraftModel : IBriefDraftModel
{
    private readonly Queue<string> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeBriefDraftModel Draft(string outputJson)
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
