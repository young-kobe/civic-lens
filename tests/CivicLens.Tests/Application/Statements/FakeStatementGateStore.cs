using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Statements;

internal sealed class FakeStatementGateStore : FakeAnalysisQueue<StatementGateRun>, IStatementGateStore
{
    public FakeStatementGateStore(params string[] articleTexts) : this(StatementGateFixtures.Observed, articleTexts) { }

    public FakeStatementGateStore(DateTimeOffset observed, params string[] articleTexts)
    {
        foreach (var text in articleTexts)
        {
            var evidence = StatementGateFixtures.Evidence(text, observed);
            Evidence[evidence.CandidateId] = evidence;
            Items.Add(new(evidence.CandidateId, StatementGateTask.Task, State.Pending, null, 0, 0, null, null));
        }
    }

    public Dictionary<string, StatementGateEvidence> Evidence { get; } = [];
    public HashSet<string> QueuedDrafts { get; } = [];

    public Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult<StatementGateEvidence?>(Evidence[candidateId]);

    public Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<IReadOnlyList<StatementGateListItem>> ListAsync(StatementGateView? view, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StatementGateListItem>>([]);

    public Task<IReadOnlyDictionary<StatementGateView, int>> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<StatementGateView, int>>(new Dictionary<StatementGateView, int>());

    public void ResumeAfterRejection(string outputJson, params string[] errors)
    {
        var entry = Items[0];
        var evidence = Evidence[entry.SubjectId];
        var run = new AnalysisRun(new string('a', 32), entry.SubjectId, StatementGateTask.Task, StatementGateTask.TaskVersion,
            StatementGateTask.Model, StatementGateTask.PromptVersion, StatementGateTask.SchemaVersion, evidence.Extraction.TextSha256, null,
            AnalysisRunOutcome.CitationRejected, AnalysisTokenUsage.None, 0, "end_turn", null, outputJson, [.. errors], "{}",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Runs.Add(new(run, StatementGateFixtures.Revision().Id, null, []));
        Items[0] = entry with { RetryOfRunId = run.RunId };
    }

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        StatementGateLifecycle.CanTransition(record, checkpoint);

    protected override AnalysisRun RunOf(StatementGateRun run) => run.Run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint.Status == State.Succeeded && StatementGateReasons.QueuesDraft(checkpoint.ErrorCode))
            QueuedDrafts.Add(entry.SubjectId);
    }
}
