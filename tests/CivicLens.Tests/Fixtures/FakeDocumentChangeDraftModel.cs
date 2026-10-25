using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeDocumentChangeDraftModel : IDocumentChangeDraftModel
{
    private readonly Queue<Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>>> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeDocumentChangeDraftModel Reply(Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> reply)
    {
        lock (gate) replies.Enqueue(reply);
        return this;
    }

    public FakeDocumentChangeDraftModel Reply(AnalysisModelResponse response) => Reply((_, _) => Task.FromResult(response));

    public FakeDocumentChangeDraftModel Draft(string outputJson, long inputTokens = 900, long outputTokens = 300) =>
        Reply(new AnalysisModelResponse("end_turn", outputJson, new(inputTokens, outputTokens, 0, 0), "req_fixture"));

    public FakeDocumentChangeDraftModel Fail(AnalysisModelFailure kind) => Reply((_, _) => throw new AnalysisModelException(kind));

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
    {
        Func<AnalysisModelRequest, CancellationToken, Task<AnalysisModelResponse>> reply;
        lock (gate)
        {
            requests.Add(request);
            if (!replies.TryDequeue(out reply!)) throw new InvalidOperationException("The fake model has no scripted reply.");
        }
        return reply(request, cancellationToken);
    }

    public static string Output(params (string HunkId, string Side, string Quote)[] citations) =>
        Output("Deadline moves to October 30", citations);

    public static string Output(string headline, (string HunkId, string Side, string Quote)[] citations,
        string[]? officialIds = null, string[]? issueIds = null, object? changeDate = null, double confidence = 0.8) => JsonSerializer.Serialize(new
        {
            headline,
            summary = "The page now gives a later deadline.",
            significance = (string?)null,
            limits = "The evidence does not show when or why the page changed.",
            institution = "Example Office",
            changeDate,
            officialIds = (officialIds ?? []).Select(id => new { id, confidence = 0.7 }),
            issueIds = (issueIds ?? []).Select(id => new { id, confidence = 0.6 }),
            citations = citations.Select(citation => new { hunkId = citation.HunkId, side = citation.Side, quote = citation.Quote }),
            confidence
        });

    public static DocumentChangeDraftEvidence Evidence(string beforeText, string afterText)
    {
        var before = new DocumentExtraction(Attempt("https://example.test/page"), "parser", beforeText);
        var after = new DocumentExtraction(Attempt("https://example.test/page"), "parser", afterText);
        var comparison = DocumentComparison.Create(before, after);
        var summary = new EligibleDocumentComparison("source", "https://example.test/page", "https://example.test/page",
            "https://example.test/page", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), comparison);
        return new(summary, before, after, null, null);
    }

    private static CapturedAttemptResult Attempt(string url) => new(Guid.NewGuid().ToString("N"), "source", url, url,
        DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/plain", []), new CaptureIdentity(new string('a', 64), 0));

    public static AnalysisTokenUsage Usage(long input, long output) => new(input, output, 0, 0);
}
