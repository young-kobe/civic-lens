using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Tests.Application.Analysis;

internal sealed class FakeAnalysisStore : IDocumentChangeAnalysisStore
{
    private readonly Lock gate = new();
    private readonly List<DocumentChangeAnalysisRecord> entries = [];
    private readonly Dictionary<string, long> reserved = [];

    public FakeAnalysisStore(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var evidence = FakeDocumentChangeDraftingModel.Evidence($"Item {index} old.\n", $"Item {index} new.\n");
            var id = evidence.Comparison.Comparison.ComparisonId;
            Evidence[id] = evidence;
            entries.Add(new(id, DocumentChangeDraftingTask.Task, State.Pending, null, 0, 0, null, null, null));
        }
        ReadEvidence = id => Task.FromResult<DocumentChangeAnalysisEvidence?>(Evidence[id]);
    }

    public Dictionary<string, DocumentChangeAnalysisEvidence> Evidence { get; } = [];
    public Func<string, Task<DocumentChangeAnalysisEvidence?>> ReadEvidence { get; set; }
    public List<AnalysisRun> Runs { get; } = [];
    public long Charged { get; set; }

    public IReadOnlyList<DocumentChangeAnalysisRecord> Entries
    {
        get { lock (gate) return [.. entries]; }
    }

    public Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<DocumentChangeAnalysisRecord>>([.. entries.Where(entry => entry.Status == State.Pending).Take(limit)]);
    }

    public Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(IReadOnlyCollection<string> comparisonIds,
        CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<DocumentChangeAnalysisRecord>>([.. entries.Where(entry => comparisonIds.Contains(entry.ComparisonId))]);
    }

    public Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken) => Task.FromResult<AnalysisRun?>(null);

    public Task<AnalysisRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Runs.SingleOrDefault(run => run.RunId == runId));
    }

    public Task<long> GetChargedTodayAsync(CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Charged);
    }

    public Task<int> RequeueFailedAsync(string? comparisonId, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<DocumentChangeAnalysisEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken) =>
        ReadEvidence(comparisonId);

    public void ResumeAfter(AnalysisRunOutcome outcome)
    {
        var entry = entries[0];
        var run = new AnalysisRun(new string('a', 32), entry.ComparisonId, DocumentChangeDraftingTask.Task, DocumentChangeDraftingTask.TaskVersion,
            DocumentChangeDraftingTask.Model, DocumentChangeDraftingTask.PromptVersion, DocumentChangeDraftingTask.SchemaVersion,
            new string('b', 64), null, outcome, AnalysisTokenUsage.None, 0, "end_turn", null, "{}", ["headline is required."], "{}",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Runs.Add(run);
        entries[0] = entry with { RetryOfRunId = run.RunId };
    }

    public Task<DocumentChangeAnalysisClaim?> TryClaimAsync(string comparisonId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var index = entries.FindIndex(entry => entry.ComparisonId == comparisonId);
            var entry = entries[index];
            if (!DocumentChangeAnalysisLifecycle.CanTransition(entry, new(entry.ComparisonId, entry.Task, entry.Status, entry.LeaseToken,
                    entry.Fence, State.Running)))
                return Task.FromResult<DocumentChangeAnalysisClaim?>(null);
            var claimed = entry with { Status = State.Running, LeaseToken = Guid.NewGuid().ToString("N"), Fence = entry.Fence + 1, ErrorCode = null };
            entries[index] = claimed;
            return Task.FromResult<DocumentChangeAnalysisClaim?>(new(claimed, claimed.LeaseToken!, claimed.Fence));
        }
    }

    public Task<bool> RenewAsync(DocumentChangeAnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<DocumentChangeAnalysisReservation> ReserveAsync(DocumentChangeAnalysisClaim claim, string runId, string inputHash,
        long tokens, long dailyTokenLimit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (Charged + tokens <= dailyTokenLimit)
            {
                Charged += tokens;
                reserved[claim.Record.ComparisonId] = tokens;
                return Task.FromResult(DocumentChangeAnalysisReservation.Reserved);
            }
        }
        return Task.FromResult(Apply(DocumentChangeAnalysisCheckpoint.From(claim, State.WaitingForBudget, "dailyTokenLimit"), null)
            ? DocumentChangeAnalysisReservation.WaitingForBudget : DocumentChangeAnalysisReservation.LeaseLost);
    }

    public Task<bool> CheckpointAsync(DocumentChangeAnalysisCheckpoint checkpoint, AnalysisRun? run, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(checkpoint, run));

    public Task<bool> ReleaseAsync(DocumentChangeAnalysisClaim claim, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(DocumentChangeAnalysisCheckpoint.From(claim, State.Pending), null));

    public Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    private bool Apply(DocumentChangeAnalysisCheckpoint checkpoint, AnalysisRun? run)
    {
        lock (gate)
        {
            var index = entries.FindIndex(entry => entry.ComparisonId == checkpoint.ComparisonId);
            var entry = entries[index];
            if (!DocumentChangeAnalysisLifecycle.CanTransition(entry, checkpoint)) return false;
            var attempts = entry.Attempts + (checkpoint.Status == State.RetryWaiting ? 1 : 0);
            if (run is not null)
            {
                Charged += run.ChargedTokens - reserved.GetValueOrDefault(entry.ComparisonId);
                reserved.Remove(entry.ComparisonId);
                Runs.Add(run);
                if (run.Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected) attempts++;
            }
            entries[index] = entry with
            {
                Status = checkpoint.Status,
                ErrorCode = checkpoint.ErrorCode,
                Attempts = attempts,
                LeaseToken = checkpoint.Status == State.Running ? entry.LeaseToken : null,
                DraftId = checkpoint.Revision?.DraftId ?? entry.DraftId
            };
            return true;
        }
    }
}
