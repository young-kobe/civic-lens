using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeHeadlineSummaryModel : IActionDraftModel, IVoteDraftModel
{
    private readonly Queue<Func<AnalysisModelRequest, Task<AnalysisModelResponse>>> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeHeadlineSummaryModel Draft(string outputJson) =>
        Reply(_ => Task.FromResult(new AnalysisModelResponse("end_turn", outputJson, new(900, 100, 0, 0), "req_fixture")));

    public FakeHeadlineSummaryModel Reply(Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply)
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
