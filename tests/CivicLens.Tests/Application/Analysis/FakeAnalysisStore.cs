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
            var evidence = FakeDocumentChangeDraftingModel.Evidence($"Item {index} old.\n", $"Item {index} new.\n");
            var id = evidence.Comparison.Comparison.ComparisonId;
            Evidence[id] = evidence;
            Items.Add(new(id, DocumentChangeDraftingTask.Task, State.Pending, null, 0, 0, null, null));
        }
        ReadEvidence = id => Task.FromResult<DocumentChangeAnalysisEvidence?>(Evidence[id]);
    }

    public Dictionary<string, DocumentChangeAnalysisEvidence> Evidence { get; } = [];
    public Func<string, Task<DocumentChangeAnalysisEvidence?>> ReadEvidence { get; set; }

    public Task<IReadOnlyList<DocumentChangeAnalysisRecord>> GetByComparisonIdsAsync(IReadOnlyCollection<string> comparisonIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentChangeAnalysisRecord>>([.. Entries.Where(entry => comparisonIds.Contains(entry.SubjectId))
            .Select(entry => new DocumentChangeAnalysisRecord(entry.SubjectId, entry.Task, entry.Status, entry.LeaseToken, entry.Fence,
                entry.Attempts, entry.RetryAt, drafts.GetValueOrDefault(entry.SubjectId), entry.ErrorCode, entry.RetryOfRunId))]);

    public Task<AnalysisRun?> GetDraftRunAsync(string draftId, CancellationToken cancellationToken) => Task.FromResult<AnalysisRun?>(null);

    public Task<DocumentChangeAnalysisEvidence?> ReadEvidenceAsync(string comparisonId, CancellationToken cancellationToken) =>
        ReadEvidence(comparisonId);

    public void ResumeAfter(AnalysisRunOutcome outcome)
    {
        var entry = Items[0];
        var run = new AnalysisRun(new string('a', 32), entry.SubjectId, DocumentChangeDraftingTask.Task, DocumentChangeDraftingTask.TaskVersion,
            DocumentChangeDraftingTask.Model, DocumentChangeDraftingTask.PromptVersion, DocumentChangeDraftingTask.SchemaVersion,
            new string('b', 64), null, outcome, AnalysisTokenUsage.None, 0, "end_turn", null, "{}", ["headline is required."], "{}",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Runs.Add(run);
        Items[0] = entry with { RetryOfRunId = run.RunId };
    }

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        DocumentChangeAnalysisLifecycle.CanTransition(record, checkpoint);

    protected override AnalysisRun RunOf(AnalysisRun run) => run;

    protected override void OnSettled(AnalysisRecord entry, AnalysisCheckpoint checkpoint)
    {
        if (checkpoint is DocumentChangeAnalysisCheckpoint { Revision: { } revision }) drafts[entry.SubjectId] = revision.DraftId;
    }
}
