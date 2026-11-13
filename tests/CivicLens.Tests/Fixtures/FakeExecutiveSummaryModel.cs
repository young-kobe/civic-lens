using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Collection;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeExecutiveSummaryModel : IExecutiveSummaryModel
{
    private readonly Queue<Func<AnalysisModelRequest, Task<AnalysisModelResponse>>> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];

    public IReadOnlyList<AnalysisModelRequest> Requests => requests;

    public FakeExecutiveSummaryModel Draft(string outputJson) =>
        Reply(_ => Task.FromResult(new AnalysisModelResponse("end_turn", outputJson, new(900, 100, 0, 0), "req_fixture")));

    public FakeExecutiveSummaryModel Reply(Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply)
    {
        replies.Enqueue(reply);
        return this;
    }

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
    {
        requests.Add(request);
        if (!replies.TryDequeue(out var reply)) throw new InvalidOperationException("The fake model has no scripted reply.");
        return reply(request);
    }
}

/// <summary>A store that holds one evidence and no queue. The measure and the step read only the evidence.</summary>
internal sealed class FakeExecutiveSummaryStore(ExecutiveSummaryEvidence? evidence) : IExecutiveSummaryStore
{
    public Task<ExecutiveSummaryEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken) => Task.FromResult(evidence);

    public Task<string?> FindRecordIdAsync(string documentNumber, CancellationToken cancellationToken) =>
        Task.FromResult(evidence?.Current.RecordId);

    public Task<ExecutiveSummaryRun?> GetRunAsync(string runId, CancellationToken cancellationToken) => Task.FromResult<ExecutiveSummaryRun?>(null);

    public Task<IReadOnlyList<ExecutiveSummaryListItem>> ListAsync(ExecutiveSummaryView? view, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<ExecutiveSummaryView, int>> CountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<AnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<long> GetChargedTodayAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> RequeueFailedAsync(string? subjectId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AnalysisClaim?> TryClaimAsync(string subjectId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> RenewAsync(AnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<AnalysisReservation> ReserveAsync(AnalysisClaim claim, string runId, string inputHash, long tokens, long dailyTokenLimit,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<bool> CheckpointAsync(AnalysisCheckpoint checkpoint, ExecutiveSummaryRun? run, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> ReleaseAsync(AnalysisClaim claim, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}
