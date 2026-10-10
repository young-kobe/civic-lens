using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Fixtures;

internal sealed class FakeDocumentChangeDraftingModel : IDocumentChangeDraftingModel
{
    private readonly Queue<Func<DocumentChangeDraftingRequest, CancellationToken, Task<DocumentChangeDraftingResponse>>> replies = new();
    private readonly List<DocumentChangeDraftingRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<DocumentChangeDraftingRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeDocumentChangeDraftingModel Reply(Func<DocumentChangeDraftingRequest, CancellationToken, Task<DocumentChangeDraftingResponse>> reply)
    {
        lock (gate) replies.Enqueue(reply);
        return this;
    }

    public FakeDocumentChangeDraftingModel Reply(DocumentChangeDraftingResponse response) => Reply((_, _) => Task.FromResult(response));

    public FakeDocumentChangeDraftingModel Draft(string outputJson, long inputTokens = 900, long outputTokens = 300) =>
        Reply(new DocumentChangeDraftingResponse("end_turn", outputJson, new(inputTokens, outputTokens, 0, 0), "req_fixture"));

    public FakeDocumentChangeDraftingModel Fail(DocumentChangeDraftingFailure kind) => Reply((_, _) => throw new DocumentChangeDraftingException(kind));

    public Task<DocumentChangeDraftingResponse> DraftAsync(DocumentChangeDraftingRequest request, CancellationToken cancellationToken)
    {
        Func<DocumentChangeDraftingRequest, CancellationToken, Task<DocumentChangeDraftingResponse>> reply;
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
        string[]? officialIds = null, string[]? issueIds = null, object? changeDate = null) => JsonSerializer.Serialize(new
        {
            headline,
            summary = "The page now gives a later deadline.",
            significance = (string?)null,
            limits = "The evidence does not show when or why the page changed.",
            institution = "Example Office",
            changeDate,
            officialIds = officialIds ?? [],
            issueIds = issueIds ?? [],
            citations = citations.Select(citation => new { hunkId = citation.HunkId, side = citation.Side, quote = citation.Quote })
        });

    public static DocumentChangeAnalysisEvidence Evidence(string beforeText, string afterText)
    {
        var before = new DocumentExtraction(Attempt("https://example.test/page"), "parser", "normalizer", beforeText);
        var after = new DocumentExtraction(Attempt("https://example.test/page"), "parser", "normalizer", afterText);
        var comparison = DocumentComparison.Create(before, after);
        var summary = new EligibleDocumentComparison("source", "https://example.test/page", "https://example.test/page",
            "https://example.test/page", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), comparison);
        return new(summary, before, after, null);
    }

    private static CapturedAttemptResult Attempt(string url) => new(Guid.NewGuid().ToString("N"), "source", url, url,
        DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/plain", []), new CaptureIdentity(new string('a', 64), 0));

    public static AnalysisTokenUsage Usage(long input, long output) => new(input, output, 0, 0);
}
