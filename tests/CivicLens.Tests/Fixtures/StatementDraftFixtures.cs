using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Statements;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Fixtures;

internal static class StatementDraftFixtures
{
    public const string Warren = "warren";
    public const string Markey = "markey";

    public const string Article = """
        WASHINGTON, October 3, 2026 - Senator Warren announced a rent cap for tenants.
        "This cap protects families," said Senator Warren. Tenant advocate Jo Rivera said, “We welcome it.”
        The bill is S. 1234 and its House companion is H.R. 56.
        """;

    public static CollectionConfiguration Configuration(DateOnly? warrenEndsBefore = null) => new()
    {
        Version = 2,
        People = [new() { Id = Warren, Name = "Elizabeth Warren" }, new() { Id = Markey, Name = "Ed Markey" }],
        Sources =
        [
            new()
            {
                Id = "source",
                Url = "https://example.test/news",
                AllowedOrigin = "https://example.test",
                AllowedPathPrefix = "/news",
                Coverage =
                [
                    new() { PersonId = Warren, EndsBefore = warrenEndsBefore },
                    new() { PersonId = Markey, StartsOn = new DateOnly(2026, 8, 1) }
                ]
            }
        ]
    };

    public static CollectionConfigurationRevision SpeakerRevision(DateOnly? warrenEndsBefore = null) =>
        CollectionConfigurationRevision.Create(Configuration(warrenEndsBefore));

    public static CollectionConfiguration SoleCoverage(string personId) => Configuration() with
    {
        Sources = [Configuration().Sources[0] with { Coverage = [new() { PersonId = personId }] }]
    };

    public static CollectionJobDefinition AdmittedDefinition(CollectionConfiguration configuration) =>
        CollectionJobDefinition.FromConfiguration(configuration, "source");

    public static CollectionJobDefinition LegacyDefinition() =>
        AdmittedDefinition(Configuration()) with { ConfigurationRevision = null, CoverageAsOf = null };

    public static ImmutableArray<AnalysisOfficial> Officials(params string[] ids) =>
        [.. ids.Select(id => new AnalysisOfficial(id, id == Warren ? "Elizabeth Warren" : "Ed Markey"))];

    public static StatementDraftEvidence DraftEvidence(string text = Article, StatementGateCarryOver? carryOver = null, string? existingDraftId = null,
        DateTimeOffset? observed = null) =>
        new(StatementGateFixtures.Evidence(text, observed), carryOver ?? new(["rent-affordability"], ["housing-and-community-development"]),
            existingDraftId, SpeakerRevision());

    public static string Output(string[]? speakers = null, (string Date, string Quote)? date = null, string[]? quotes = null,
        string[]? otherSpeakers = null, double confidence = 0.8, string headline = "Warren announces a rent cap") =>
        JsonSerializer.Serialize(Fields(speakers, date, quotes ?? ["a rent cap for tenants"], otherSpeakers ?? [], confidence, headline));

    public static Dictionary<string, object?> Fields(string[]? speakers, (string Date, string Quote)? date, string[] quotes,
        string[] otherSpeakers, double confidence, string headline)
    {
        var fields = new Dictionary<string, object?>
        {
            ["headline"] = headline,
            ["summary"] = "The senator announced a rent cap for tenants.",
            ["statementDate"] = date is { } stated ? new { date = stated.Date, quote = stated.Quote, confidence = 0.9 } : null,
            ["otherSpeakers"] = otherSpeakers.Select(name => new { name, confidence = 0.7 }),
            ["quotations"] = quotes.Select(quote => new { quote, confidence = 0.6 }),
            ["confidence"] = confidence
        };
        if (speakers is not null) fields["speakerOfficialIds"] = speakers.Select(id => new { id, confidence = 0.9 });
        return fields;
    }
}

internal sealed class FakeStatementDraftingModel : IStatementDraftingModel
{
    private readonly Queue<Func<AnalysisModelRequest, Task<AnalysisModelResponse>>> replies = new();
    private readonly List<AnalysisModelRequest> requests = [];
    private readonly Lock gate = new();

    public IReadOnlyList<AnalysisModelRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    public FakeStatementDraftingModel Draft(string outputJson) =>
        Reply(_ => Task.FromResult(new AnalysisModelResponse("end_turn", outputJson, new(900, 100, 0, 0), "req_fixture")));

    public FakeStatementDraftingModel Fail(AnalysisModelFailure kind) => Reply(_ => throw new AnalysisModelException(kind));

    public FakeStatementDraftingModel Reply(Func<AnalysisModelRequest, Task<AnalysisModelResponse>> reply)
    {
        lock (gate) replies.Enqueue(reply);
        return this;
    }

    public Task<AnalysisModelResponse> DraftAsync(AnalysisModelRequest request, CancellationToken cancellationToken)
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
