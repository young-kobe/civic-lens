using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Briefs;

internal sealed class FakeBriefCheckStore : FakeAnalysisQueue<BriefCheckRun>, IBriefCheckStore
{
    public FakeBriefCheckStore(BriefCheckEvidence? evidence)
    {
        Evidence = evidence;
        Items.Add(new(BriefFixtures.RecordId, BriefCheckTask.Task, State.Pending, null, 0, 0, null, null));
    }

    public BriefCheckEvidence? Evidence { get; }

    public Task<BriefCheckEvidence?> ReadEvidenceAsync(string recordId, CancellationToken cancellationToken) => Task.FromResult(Evidence);

    protected override bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        BriefCheckLifecycle.CanTransition(record, checkpoint);

    protected override CivicLens.Core.Analysis.AnalysisRun RunOf(BriefCheckRun run) => run.Run;
}

internal sealed class FakeBriefCheckModel : IBriefCheckModel
{
    private readonly Queue<string> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeBriefCheckModel Check(string outputJson)
    {
        lock (gate) replies.Enqueue(outputJson);
        return this;
    }

    public Task<AnalysisModelResponse> CheckAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
    {
        string reply;
        lock (gate)
        {
            requests.Add(request);
            if (!replies.TryDequeue(out reply!)) throw new InvalidOperationException("The fake model has no scripted reply.");
        }
        return Task.FromResult(new AnalysisModelResponse("end_turn", reply, new(900, 100, 0, 0), "req_fixture"));
    }

    public static string Output(string recommendation, params (int Paragraph, string Code, string? CitationId, string Note)[] flags) =>
        JsonSerializer.Serialize(new
        {
            recommendation,
            flags = flags.Select(flag => new { paragraphIndex = flag.Paragraph, code = flag.Code, citationId = flag.CitationId, note = flag.Note })
        });
}

internal static class BriefCheckFixtures
{
    public static BriefCheckEvidence Evidence(BriefCheckSettled? settled = null, ImmutableArray<BriefRecord> notCited = default) => new(
        BriefFixtures.Revision(),
        new Dictionary<string, BriefRecord>
        {
            ["R1"] = BriefFixtures.Statement(new string('1', 64), "Warren on rent", 1, "warren"),
            ["R2"] = BriefFixtures.DocumentChange(new string('2', 32), "A rule changed", 3)
        }.ToImmutableDictionary(),
        notCited.IsDefault ? [] : notCited, settled);
}
