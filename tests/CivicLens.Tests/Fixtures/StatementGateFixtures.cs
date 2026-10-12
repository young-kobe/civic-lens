using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Application.Statements;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Fixtures;

internal static class StatementGateFixtures
{
    public const string RentIssue = "rent-affordability";
    public const string RetiredIssue = "retired-rule";
    public const string FutureIssue = "future-rule";
    public const string HousingArea = "housing-and-community-development";
    public const string HealthArea = "health";
    public static readonly DateTimeOffset Observed = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public static IssueConfiguration Configuration(string rentDefinition = "Limits on rent increases for tenants.") => new()
    {
        PolicyAreas = [new() { Id = HousingArea, Name = "Housing and Community Development" }, new() { Id = HealthArea, Name = "Health" }],
        Issues =
        [
            new() { Id = RentIssue, Name = "Rent affordability", Definition = rentDefinition, PolicyAreaIds = [HousingArea],
                StartsOn = new DateOnly(2026, 1, 1) },
            new() { Id = RetiredIssue, Name = "Retired rule", Definition = "A rule that ended in March.", PolicyAreaIds = [HealthArea],
                StartsOn = new DateOnly(2026, 1, 1), EndsBefore = new DateOnly(2026, 3, 1) },
            new() { Id = FutureIssue, Name = "Future rule", Definition = "A rule that starts in December.", PolicyAreaIds = [HealthArea],
                StartsOn = new DateOnly(2026, 12, 1) }
        ]
    };

    public static IssueConfigurationRevision Revision(string rentDefinition = "Limits on rent increases for tenants.") =>
        IssueConfigurationRevision.Create(Configuration(rentDefinition));

    public static StatementGateEvidence Evidence(string text, DateTimeOffset? observed = null, string url = "https://example.test/news/release")
    {
        var attempt = new CapturedAttemptResult(Guid.NewGuid().ToString("N"), "source", url, url, observed ?? Observed,
            new CollectionResponse(200, null, null, "text/plain", []), new CaptureIdentity(new string('a', 64), 0));
        var extraction = new DocumentExtraction(attempt, "parser", "normalizer", text);
        return new(extraction.ExtractionId, extraction);
    }

    public static StatementGateRubric Rubric(DateTimeOffset? observed = null) =>
        StatementGateRubric.Create(Revision().Id, Configuration(), DateOnly.FromDateTime((observed ?? Observed).UtcDateTime));

    public static string Output(string[]? areas = null, double confidence = 0.8, params (string Id, string Quote)[] issues) =>
        JsonSerializer.Serialize(new
        {
            trackedIssues = issues.Select(issue => new { id = issue.Id, quote = issue.Quote, confidence = 0.9 }),
            policyAreas = (areas ?? []).Select(id => new { id, confidence = 0.7 }),
            confidence
        });
}

internal sealed class FakeStatementGateModel : IStatementGateModel
{
    private readonly Queue<Func<AnalysisModelRequest, Task<AnalysisModelResponse>>> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeStatementGateModel Classify(string outputJson) =>
        Reply(_ => Task.FromResult(new AnalysisModelResponse("end_turn", outputJson, new(900, 100, 0, 0), "req_fixture")));

    public FakeStatementGateModel Fail(AnalysisModelFailure kind) =>
        Reply(_ => throw new AnalysisModelException(kind));

    public FakeStatementGateModel Reply(Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply)
    {
        lock (gate) replies.Enqueue(reply);
        return this;
    }

    public Task<AnalysisModelResponse> ClassifyAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
    {
        Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply;
        lock (gate)
        {
            requests.Add(request);
            if (!replies.TryDequeue(out reply!)) throw new InvalidOperationException("The fake model has no scripted reply.");
        }
        return reply(request);
    }
}
