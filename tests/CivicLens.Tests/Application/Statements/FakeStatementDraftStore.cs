using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Core.Statements;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Statements;

internal sealed class FakeStatementDraftStore : FakeAnalysisQueue<StatementDraftRun>, IStatementDraftStore
{
    public FakeStatementDraftStore(StatementDraftEvidence evidence)
    {
        Evidence = evidence;
        Items.Add(new(evidence.Article.CandidateId, StatementDraftTask.Task, State.Pending, null, 0, 0, null, null));
    }

    public StatementDraftEvidence Evidence { get; }
    public List<StatementDraftRevision> Revisions { get; } = [];
    public List<string> DraftsWithoutRevision { get; } = [];
    public List<string> CodeApproved { get; } = [];

    public Task<StatementDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken) =>
        Task.FromResult<StatementDraftEvidence?>(Evidence);

    public Task<IReadOnlyList<StatementDraftListItem>> ListAsync(StatementDraftView? view, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StatementDraftListItem>>([]);

    public Task<IReadOnlyDictionary<StatementDraftView, int>> CountAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<StatementDraftView, int>>(new Dictionary<StatementDraftView, int>());

    public void ResumeAfterRejection(string outputJson, params string[] errors)
    {
        var entry = Items[0];
        var run = new AnalysisRun(new string('a', 32), entry.SubjectId, StatementDraftTask.Task, StatementDraftTask.TaskVersion,
            StatementDraftTask.Model, StatementDraftTask.PromptVersion, StatementDraftTask.SchemaVersion,
            Evidence.Article.Extraction.TextSha256, null, AnalysisRunOutcome.CitationRejected, AnalysisTokenUsage.None, 0, "end_turn", null,
            outputJson, [.. errors], "{}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Runs.Add(new(run, null));
        Items[0] = entry with { RetryOfRunId = run.RunId };
    }

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        StatementDraftLifecycle.CanTransition(record, checkpoint);

    protected override AnalysisRun RunOf(StatementDraftRun run) => run.Run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is StatementDraftCheckpoint { Revision: { } revision } settled)
        {
            Revisions.Add(revision);
            if (settled.CodeApproved) CodeApproved.Add(revision.CandidateId);
        }
        if (checkpoint.Status == State.Succeeded && checkpoint.ErrorCode == StatementDraftErrorCodes.OverByteCap)
            DraftsWithoutRevision.Add(entry.SubjectId);
    }
}
