using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Analysis;

internal sealed class FakeAnalysisStore : FakeAnalysisQueue<AnalysisRun>, IDocumentChangeAnalysisStore
{
    private readonly Dictionary<string, string> drafts = [];

    public FakeAnalysisStore(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var evidence = FakeDocumentChangeDraftModel.Evidence($"Item {index} old.\n", $"Item {index} new.\n");
            var id = evidence.Comparison.Comparison.ComparisonId;
            Evidence[id] = evidence;
            Items.Add(new(id, DocumentChangeDraftTask.Task, State.Pending, null, 0, 0, null, null));
        }
        ReadEvidence = id => Task.FromResult<DocumentChangeDraftEvidence?>(Evidence[id]);
    }

    public Dictionary<string, DocumentChangeDraftEvidence> Evidence { get; } = [];
    public Func<string, Task<DocumentChangeDraftEvidence?>> ReadEvidence { get; set; }

    public Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(IReadOnlyCollection<string> comparisonIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentChangeAnalysisRecord>>([.. Entries.Where(entry => comparisonIds.Contains(entry.SubjectId))
            .Select(entry => new DocumentChangeAnalysisRecord(entry.SubjectId, entry.Task, entry.Status, entry.LeaseToken, entry.Fence,
                entry.Attempts, entry.RetryAt, drafts.GetValueOrDefault(entry.SubjectId), entry.ErrorCode, entry.RetryOfRunId))]);

    public Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken) => Task.FromResult<AnalysisRun?>(null);

    public Task<DocumentChangeDraftEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken) =>
        ReadEvidence(comparisonId);

    public void ResumeAfter(AnalysisRunOutcome outcome)
    {
        var entry = Items[0];
        var run = new AnalysisRun(new string('a', 32), entry.SubjectId, DocumentChangeDraftTask.Task, DocumentChangeDraftTask.TaskVersion,
            DocumentChangeDraftTask.Model, DocumentChangeDraftTask.PromptVersion, DocumentChangeDraftTask.SchemaVersion,
            new string('b', 64), null, outcome, AnalysisTokenUsage.None, 0, "end_turn", null, "{}", ["headline is required."], "{}",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Runs.Add(run);
        Items[0] = entry with { RetryOfRunId = run.RunId };
    }

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        DocumentChangeDraftLifecycle.CanTransition(record, checkpoint);

    protected override AnalysisRun RunOf(AnalysisRun run) => run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is DocumentChangeDraftCheckpoint { Revision: { } revision }) drafts[entry.SubjectId] = revision.DraftId;
    }
}
