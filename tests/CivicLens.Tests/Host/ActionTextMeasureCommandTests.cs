using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Host.Actions;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.Fixtures.ActionBillTexts;

namespace CivicLens.Tests.Host;

public sealed class ActionTextMeasureCommandTests : IDisposable
{
    private static readonly string BillsFile = Path.Combine(RepositoryRoot.FullName, "config", "action-text-measure-bills.json");
    private readonly string directory = Directory.CreateTempSubdirectory("text-measure").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData]
    [InlineData("1000000")]
    [InlineData("0", "bills.json")]
    [InlineData("-5", "bills.json")]
    [InlineData("5.50", "bills.json")]
    [InlineData("$5", "bills.json")]
    public async Task TheCommandRefusesToStartWithoutAWholePositiveTokenCapAndCreatesNoMeasure(params string[] arguments)
    {
        var created = false;

        var exit = await ExecuteAsync(["actions", "text", "measure", .. arguments], () => { created = true; return Measure(); });

        Assert.Equal(2, exit.Code);
        Assert.False(created);
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public async Task TheCommandWritesTheReportUnderTheArtifactDirectoryAndPrintsTheTotals()
    {
        var exit = await ExecuteAsync(["actions", "text", "measure", "10000000", BillsFile], Measure);

        Assert.Equal(0, exit.Code);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(directory, ActionTextMeasureCommand.ReportDirectory)));
        using var report = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(ActionDraftTask.TextPromptVersion, report.RootElement.GetProperty("promptVersion").GetString());
        Assert.Equal(20, report.RootElement.GetProperty("bills").GetArrayLength());
        Assert.Equal(20, report.RootElement.GetProperty("totals").GetProperty("skipped").GetInt32() +
            report.RootElement.GetProperty("totals").GetProperty("accepted").GetInt32());
        Assert.Contains(file.Replace("\\", "\\\\", StringComparison.Ordinal), exit.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelStillWritesTheReportThenThrowsTheCancel()
    {
        var path = Path.Combine(directory, "one-bill.json");
        File.WriteAllText(path, "[\"119-hr-1\"]");

        await Assert.ThrowsAsync<ActionTextMeasureCanceledException>(() =>
            ExecuteAsync(["actions", "text", "measure", "10000000", path], () => Measure(new FakeHeadlineSummaryModel().Reply(_ => throw new OperationCanceledException()))));

        var file = Assert.Single(Directory.GetFiles(Path.Combine(directory, ActionTextMeasureCommand.ReportDirectory)));
        Assert.Equal(0, JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("bills").GetArrayLength());
    }

    [Fact]
    public async Task TheFixedBillListHoldsTwentyDistinctValidBillIdsThatTheMeasureReadsBack()
    {
        var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(BillsFile))!;

        Assert.Equal(20, ids.Length);
        Assert.Equal(20, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(id, ActionTextMeasure.IdOf(ActionTextMeasure.TryParseId(id)!)));
    }

    [Theory]
    [InlineData("""["119-hr-1","119-hr-1"]""")]
    [InlineData("""["119-xx-1"]""")]
    [InlineData("""["119-HR-1"]""")]
    [InlineData("""["119-hr-01"]""")]
    [InlineData("[]")]
    public async Task ABillsFileWithABadOrRepeatedIdStopsTheCommandBeforeAnyMeasure(string json)
    {
        var path = Path.Combine(directory, "bills.json");
        await File.WriteAllTextAsync(path, json);
        var created = false;

        var exit = await ExecuteAsync(["actions", "text", "measure", "1000", path], () => { created = true; return Measure(); });

        Assert.Equal(2, exit.Code);
        Assert.False(created);
    }

    [Fact]
    public async Task TheExecutiveFormWritesTheSameKindOfReportForTheDocumentsOfItsFile()
    {
        var path = Path.Combine(directory, "documents.json");
        await File.WriteAllTextAsync(path, """["2025-01953"]""");
        var model = new FakeExecutiveSummaryModel();

        var exit = await ExecuteAsync(["executive-actions", "text", "measure", "1000000", path], Measure,
            () => new ExecutiveSummaryMeasure(new FakeExecutiveSummaryStore(null), new ExecutiveSummaryStep(new FakeExecutiveSummaryStore(null), model, new ExecutiveSummarySettings(10_000_000, 1))));

        Assert.Equal(0, exit.Code);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(directory, ActionTextMeasureCommand.ReportDirectory)));
        using var report = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal(ExecutiveSummaryTask.PromptVersion, report.RootElement.GetProperty("promptVersion").GetString());
        Assert.Equal(("2025-01953", ActionTextMeasureReason.NoRecord), (report.RootElement.GetProperty("bills")[0].GetProperty("billId").GetString(),
            report.RootElement.GetProperty("bills")[0].GetProperty("reason").GetString()));
        Assert.Empty(model.Requests);
    }

    [Theory]
    [InlineData("""["2025-01953","2025-01953"]""")]
    [InlineData("""["not a number"]""")]
    [InlineData("[]")]
    public async Task ADocumentsFileWithABadOrRepeatedNumberStopsTheExecutiveFormBeforeAnyMeasure(string json)
    {
        var path = Path.Combine(directory, "documents.json");
        await File.WriteAllTextAsync(path, json);
        var created = false;

        var exit = await ExecuteAsync(["executive-actions", "text", "measure", "1000", path], Measure, () =>
        {
            created = true;
            return null!;
        });

        Assert.Equal(2, exit.Code);
        Assert.False(created);
    }

    private async Task<(int Code, string Output)> ExecuteAsync(string[] args, Func<ActionTextMeasure> createMeasure,
        Func<ExecutiveSummaryMeasure>? createExecutive = null)
    {
        var output = new StringWriter();
        var error = Console.Error;
        Console.SetError(TextWriter.Null);
        try
        {
            var code = await ActionTextMeasureCommand.ExecuteAsync(args, createMeasure, directory, output, TimeProvider.System, default, createExecutive);
            return (code, output.ToString());
        }
        finally
        {
            Console.SetError(error);
        }
    }

    private static ActionTextMeasure Measure() => Measure(new FakeHeadlineSummaryModel().Draft(Output()));

    private static ActionTextMeasure Measure(FakeHeadlineSummaryModel model)
    {
        var world = ActionBillWorld.Create(summaries: []);
        var store = new FakeActionDraftStore(world.Evidence() with { BillText = Of() });
        return new(store, new ActionDraftStep(store, world.Reader(), model, new ActionDraftSettings(10_000_000, 1, true), BillGateFixtures.Revision()));
    }
}
