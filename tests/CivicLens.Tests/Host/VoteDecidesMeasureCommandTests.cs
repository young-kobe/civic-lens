using System.Text.Json;
using CivicLens.Application.Votes;
using CivicLens.Core.Votes;
using CivicLens.Host.Votes;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Application.Votes;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.VoteWorld;

namespace CivicLens.Tests.Host;

public sealed class VoteDecidesMeasureCommandTests : IDisposable
{
    private static readonly string VotesFile = Path.Combine(RepositoryRoot.FullName, "config", "vote-decides-measure-votes.json");
    private readonly string directory = Directory.CreateTempSubdirectory("vote-measure").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData]
    [InlineData("1000000")]
    [InlineData("0", "votes.json")]
    [InlineData("-5", "votes.json")]
    [InlineData("5.50", "votes.json")]
    public async Task TheCommandRefusesToStartWithoutAWholePositiveTokenCapAndCreatesNoMeasure(params string[] arguments)
    {
        var created = false;

        var exit = await ExecuteAsync(["votes", "decides", "measure", .. arguments], () => { created = true; return Measure(); });

        Assert.Equal(2, exit.Code);
        Assert.False(created);
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public async Task TheCommandWritesTheReportUnderTheArtifactDirectoryAndPrintsTheTotals()
    {
        var exit = await ExecuteAsync(["votes", "decides", "measure", "10000000", VotesFile], Measure);

        Assert.Equal(0, exit.Code);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(directory, VoteDecidesMeasureCommand.ReportDirectory)));
        using var report = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(VoteDraftTask.PromptVersion, report.RootElement.GetProperty("promptVersion").GetString());
        Assert.Equal(20, report.RootElement.GetProperty("votes").GetArrayLength());
        Assert.Equal((1, 19), (report.RootElement.GetProperty("totals").GetProperty("accepted").GetInt32(), report.RootElement.GetProperty("totals").GetProperty("skipped").GetInt32()));
        Assert.Contains(file.Replace("\\", "\\\\", StringComparison.Ordinal), exit.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelStillWritesTheReportThenThrowsTheCancel()
    {
        var path = Path.Combine(directory, "one-vote.json");
        File.WriteAllText(path, "[\"house-119-1-100\"]");

        await Assert.ThrowsAsync<VoteDecidesMeasureCanceledException>(() =>
            ExecuteAsync(["votes", "decides", "measure", "10000000", path], () => Measure(new FakeHeadlineSummaryModel().Reply(_ => throw new OperationCanceledException()))));

        var file = Assert.Single(Directory.GetFiles(Path.Combine(directory, VoteDecidesMeasureCommand.ReportDirectory)));
        Assert.Equal(0, JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("votes").GetArrayLength());
    }

    [Fact]
    public void TheFixedVoteListHoldsTwentyDistinctValidIdsTenForEachChamber()
    {
        var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(VotesFile))!;

        Assert.Equal(20, ids.Length);
        Assert.Equal(20, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(id, VoteMeasureRoll.TryParse(id)!.Id));
        Assert.Equal((10, 10), (ids.Count(id => VoteMeasureRoll.TryParse(id)!.Chamber == Chamber.House), ids.Count(id => VoteMeasureRoll.TryParse(id)!.Chamber == Chamber.Senate)));
    }

    [Theory]
    [InlineData("""["house-119-1-1","house-119-1-1"]""")]
    [InlineData("""["house-119-3-1"]""")]
    [InlineData("""["House-119-1-1"]""")]
    [InlineData("[]")]
    public async Task AVotesFileWithABadOrRepeatedIdStopsTheCommandBeforeAnyMeasure(string json)
    {
        var path = Path.Combine(directory, "votes.json");
        await File.WriteAllTextAsync(path, json);
        var created = false;

        var exit = await ExecuteAsync(["votes", "decides", "measure", "1000", path], () => { created = true; return Measure(); });

        Assert.Equal(2, exit.Code);
        Assert.False(created);
    }

    [Theory]
    [InlineData("votes", "decides", "measure")]
    [InlineData("votes", "decides")]
    public void OnlyTheDecidesCommandOfTheVotesGroupMatches(params string[] args) => Assert.True(VoteDecidesMeasureCommand.Matches(args));

    [Fact]
    public void TheOtherVoteCommandsDoNotMatch() => Assert.False(VoteDecidesMeasureCommand.Matches(["votes", "draft", "status"]));

    private async Task<(int Code, string Output)> ExecuteAsync(string[] args, Func<VoteDecidesMeasure> createMeasure)
    {
        var output = new StringWriter();
        var error = Console.Error;
        Console.SetError(TextWriter.Null);
        try
        {
            var code = await VoteDecidesMeasureCommand.ExecuteAsync(args, createMeasure, directory, output, TimeProvider.System, default);
            return (code, output.ToString());
        }
        finally
        {
            Console.SetError(error);
        }
    }

    private static VoteDecidesMeasure Measure() => Measure(new FakeHeadlineSummaryModel().Draft(HeadlineSummaryOutputs.Output("The House voted on a concurrent resolution",
        "The House decided whether to agree to a Senate amendment.", issueIds: ["rent"],
        decides: ("The bill gives $50 million to rent aid.", "gives $50 million to the Department of Housing"))));

    private static VoteDecidesMeasure Measure(FakeHeadlineSummaryModel model)
    {
        var world = Create(summaryHtml: "This bill gives $50 million to the Department of Housing for rent aid in 2026.");
        var store = new FakeVoteDraftStore(world.Evidence());
        return new(store, new VoteDraftStep(store, world.Reader(), model, new VoteDraftSettings(10_000_000, concurrency: 1), BillGateFixtures.Revision()));
    }
}
