using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.DocumentChangeAnalysisState;

namespace CivicLens.Tests.Application.Statements;

internal sealed class FakeStatementAnalysisStore : IStatementAnalysisStore
{
    private readonly Lock gate = new();
    private readonly List<StatementAnalysisRecord> entries = [];
    private readonly Dictionary<string, long> reserved = [];

    public FakeStatementAnalysisStore(params string[] articleTexts) : this(StatementGateFixtures.Observed, articleTexts) { }

    public FakeStatementAnalysisStore(DateTimeOffset observed, params string[] articleTexts)
    {
        foreach (var text in articleTexts)
        {
            var evidence = StatementGateFixtures.Evidence(text, observed);
            Evidence[evidence.CandidateId] = evidence;
            entries.Add(new(evidence.CandidateId, StatementGateTask.Task, State.Pending, null, 0, 0, null, null));
        }
    }

    public Dictionary<string, StatementGateEvidence> Evidence { get; } = [];
    public List<StatementGateRun> Runs { get; } = [];
    public HashSet<string> QueuedDrafts { get; } = [];
    public long Charged { get; set; }

    public IReadOnlyList<StatementAnalysisRecord> Entries
    {
        get { lock (gate) return [.. entries]; }
    }

    public Task<IReadOnlyList<StatementAnalysisRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<StatementAnalysisRecord>>([.. entries.Where(entry => entry.Status == State.Pending).Take(limit)]);
    }

    public Task<StatementGateRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Runs.SingleOrDefault(run => run.Run.RunId == runId));
    }

    public Task<long> GetChargedTodayAsync(CancellationToken cancellationToken)
    {
        lock (gate) return Task.FromResult(Charged);
    }

    public Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult<StatementGateEvidence?>(Evidence[candidateId]);

    public Task<StatementAnalysisClaim?> TryClaimAsync(string candidateId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var index = entries.FindIndex(entry => entry.CandidateId == candidateId);
            var entry = entries[index];
            if (!StatementAnalysisLifecycle.CanTransition(entry, new(entry.CandidateId, entry.Task, entry.Status, entry.LeaseToken,
                    entry.Fence, State.Running)))
                return Task.FromResult<StatementAnalysisClaim?>(null);
            var claimed = entry with { Status = State.Running, LeaseToken = Guid.NewGuid().ToString("N"), Fence = entry.Fence + 1, ErrorCode = null };
            entries[index] = claimed;
            return Task.FromResult<StatementAnalysisClaim?>(new(claimed, claimed.LeaseToken!, claimed.Fence));
        }
    }

    public Task<bool> RenewAsync(StatementAnalysisClaim claim, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Task<DocumentChangeAnalysisReservation> ReserveAsync(StatementAnalysisClaim claim, string runId, string inputHash,
        long tokens, long dailyTokenLimit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (Charged + tokens <= dailyTokenLimit)
            {
                Charged += tokens;
                reserved[claim.Record.CandidateId] = tokens;
                return Task.FromResult(DocumentChangeAnalysisReservation.Reserved);
            }
        }
        return Task.FromResult(Apply(StatementAnalysisCheckpoint.From(claim, State.WaitingForBudget, "dailyTokenLimit"), null)
            ? DocumentChangeAnalysisReservation.WaitingForBudget : DocumentChangeAnalysisReservation.LeaseLost);
    }

    public Task<bool> CheckpointAsync(StatementAnalysisCheckpoint checkpoint, StatementGateRun? run, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(checkpoint, run));

    public Task<bool> ReleaseAsync(StatementAnalysisClaim claim, CancellationToken cancellationToken) =>
        Task.FromResult(Apply(StatementAnalysisCheckpoint.From(claim, State.Pending), null));

    public Task<int> ReleaseBudgetWaitsAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var released = 0;
            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Status != State.WaitingForBudget) continue;
                entries[index] = entries[index] with { Status = State.Pending, ErrorCode = null };
                released++;
            }
            return Task.FromResult(released);
        }
    }

    public Task<int> RequeueFailedAsync(string? candidateId, CancellationToken cancellationToken) => Task.FromResult(0);
    public Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyList<StatementGateListItem>> ListAsync(StatementGateView? view, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StatementGateListItem>>([]);

    public Task<IReadOnlyDictionary<StatementGateView, int>> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<StatementGateView, int>>(new Dictionary<StatementGateView, int>());

    public void ResumeAfterRejection(string outputJson, params string[] errors)
    {
        var entry = entries[0];
        var evidence = Evidence[entry.CandidateId];
        var run = new AnalysisRun(new string('a', 32), entry.CandidateId, StatementGateTask.Task, StatementGateTask.TaskVersion,
            StatementGateTask.Model, StatementGateTask.PromptVersion, StatementGateTask.SchemaVersion, evidence.Extraction.TextSha256, null,
            AnalysisRunOutcome.CitationRejected, AnalysisTokenUsage.None, 0, "end_turn", null, outputJson, [.. errors], "{}",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Runs.Add(new(run, StatementGateFixtures.Revision().Id, null, []));
        entries[0] = entry with { RetryOfRunId = run.RunId };
    }

    private bool Apply(StatementAnalysisCheckpoint checkpoint, StatementGateRun? gateRun)
    {
        lock (gate)
        {
            var index = entries.FindIndex(entry => entry.CandidateId == checkpoint.CandidateId);
            var entry = entries[index];
            if (!StatementAnalysisLifecycle.CanTransition(entry, checkpoint)) return false;
            var attempts = entry.Attempts + (checkpoint.Status == State.RetryWaiting ? 1 : 0);
            if (gateRun is not null)
            {
                Charged += gateRun.Run.ChargedTokens - reserved.GetValueOrDefault(entry.CandidateId);
                reserved.Remove(entry.CandidateId);
                Runs.Add(gateRun);
                if (gateRun.Run.Outcome is AnalysisRunOutcome.CitationRejected or AnalysisRunOutcome.OutputRejected) attempts++;
            }
            if (checkpoint.Status == State.Succeeded && StatementGateReasons.QueuesDraft(checkpoint.ErrorCode))
                QueuedDrafts.Add(entry.CandidateId);
            entries[index] = entry with
            {
                Status = checkpoint.Status,
                ErrorCode = checkpoint.ErrorCode,
                Attempts = attempts,
                LeaseToken = checkpoint.Status == State.Running ? entry.LeaseToken : null,
                RetryOfRunId = checkpoint.Status == State.Running && gateRun is not null ? gateRun.Run.RunId
                    : checkpoint.Status == State.Running ? entry.RetryOfRunId : null
            };
            return true;
        }
    }
}
