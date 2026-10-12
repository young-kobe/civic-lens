using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Analysis;

internal abstract class FakeAnalysisQueue<TRun> : IAnalysisQueue<TRun> where TRun : class
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, long> reserved = [];

    protected List<AnalysisRecord> Items { get; } = [];

    public List<TRun> Runs { get; } = [];
    public long Charged { get; set; }

    public IReadOnlyList<AnalysisRecord> Entries
    {
        get { lock (gate) return [.. Items]; }
    }

    public Task<IReadOnlyList<AnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<AnalysisRecord>>([.. Items.Where(entry => entry.Status == State.Pending).Take(limit)]);
    }

    public Task<TRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Runs.SingleOrDefault(run => RunOf(run).RunId == runId));
    }

    public Task<long> GetChargedTodayAsync(CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Charged);
    }

    public Task<int> RequeueFailedAsync(string? subjectId, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<AnalysisClaim?> TryClaimAsync(string subjectId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var index = Items.FindIndex(entry => entry.SubjectId == subjectId);
            var entry = Items[index];
            if (!CanTransition(entry, new AnalysisCheckpoint(entry.SubjectId, entry.Task, entry.Status, entry.LeaseToken, entry.Fence, State.Running)))
                return Task.FromResult<AnalysisClaim?>(null);
            var claimed = entry with { Status = State.Running, LeaseToken = Guid.NewGuid().ToString("N"), Fence = entry.Fence + 1, ErrorCode = null };
            Items[index] = claimed;
            return Task.FromResult<AnalysisClaim?>(new(claimed, claimed.LeaseToken!, claimed.Fence));
        }
    }

    public Task<bool> RenewAsync(AnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<AnalysisReservation> ReserveAsync(AnalysisClaim claim, string runId, string inputHash, long tokens,
        long dailyTokenLimit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (Charged + tokens <= dailyTokenLimit)
            {
                Charged += tokens;
                reserved[claim.Record.SubjectId] = tokens;
                return Task.FromResult(AnalysisReservation.Reserved);
            }
        }
        return Task.FromResult(Apply(AnalysisCheckpoint.From(claim, State.WaitingForBudget, "dailyTokenLimit"), null)
            ? AnalysisReservation.WaitingForBudget : AnalysisReservation.LeaseLost);
    }

    public Task<bool> CheckpointAsync(AnalysisCheckpoint checkpoint, TRun? run, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(checkpoint, run));

    public Task<bool> ReleaseAsync(AnalysisClaim claim, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(AnalysisCheckpoint.From(claim, State.Pending), null));

    public Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var released = 0;
            for (var index = 0; index < Items.Count; index++)
            {
                if (Items[index].Status != State.WaitingForBudget) continue;
                Items[index] = Items[index] with { Status = State.Pending, ErrorCode = null };
                released++;
            }
            return Task.FromResult(released);
        }
    }

    protected abstract bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint);

    protected abstract AnalysisRun RunOf(TRun run);

    protected virtual void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint) { }

    private bool Apply(AnalysisCheckpoint checkpoint, TRun? run)
    {
        lock (gate)
        {
            var index = Items.FindIndex(entry => entry.SubjectId == checkpoint.SubjectId);
            var entry = Items[index];
            if (!CanTransition(entry, checkpoint)) return false;
            var attempts = entry.Attempts + (checkpoint.Status == State.RetryWaiting ? 1 : 0);
            if (run is not null)
            {
                var settled = RunOf(run);
                Charged += settled.ChargedTokens - reserved.GetValueOrDefault(entry.SubjectId);
                reserved.Remove(entry.SubjectId);
                Runs.Add(run);
                if (settled.Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected) attempts++;
            }
            OnSettled(entry, checkpoint);
            Items[index] = entry with
            {
                Status = checkpoint.Status,
                ErrorCode = checkpoint.ErrorCode,
                Attempts = attempts,
                LeaseToken = checkpoint.Status == State.Running ? entry.LeaseToken : null,
                RetryOfRunId = checkpoint.Status == State.Running && run is not null ? RunOf(run).RunId
                    : checkpoint.Status == State.Running ? entry.RetryOfRunId : null
            };
            return true;
        }
    }
}
