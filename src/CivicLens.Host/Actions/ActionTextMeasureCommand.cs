using System.Globalization;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Host.Collection;

namespace CivicLens.Host.Actions;

/// <summary>The owner command that measures the bill text prompt on a fixed list of bills, or the executive summary prompt on a fixed list of
/// Federal Register documents. It writes a report and no database row.</summary>
public static class ActionTextMeasureCommand
{
    public const string Usage = "Use actions text measure <token-cap> <bills-file>. The token cap is a whole number of tokens. " +
        "The bills file holds a JSON array of IDs such as 119-hr-1943. Use config/action-text-measure-bills.json. " +
        "Use executive-actions text measure <token-cap> <documents-file> for the executive summary. The documents file holds a JSON array of document numbers such as 2025-01953.";
    public const string ReportDirectory = "action-text-measure";

    public static bool Matches(string[] args) => args is ["actions", "text", ..] or ["executive-actions", "text", "measure", ..];

    /// <summary><paramref name="createExecutiveMeasure"/> serves the executive form. A command that never gets the executive form may pass null.</summary>
    public static Task<int> ExecuteAsync(string[] args, Func<ActionTextMeasure> createMeasure, string artifactDirectory, TextWriter output,
        TimeProvider clock, CancellationToken cancellationToken, Func<ExecutiveSummaryMeasure>? createExecutiveMeasure = null) =>
        OwnerCommand.RunAsync(async () =>
        {
            var (tokenCap, idsFile) = ReadArguments(args);
            Func<Task<ActionTextMeasureReport>> run = args[0] == "actions"
                ? RunBills(createMeasure, BillIdsFile.Read(idsFile), tokenCap, cancellationToken)
                : RunDocuments(createExecutiveMeasure ?? throw new ArgumentException(Usage), BillIdsFile.ReadDocumentNumbers(idsFile), tokenCap, cancellationToken);
            try
            {
                var report = await run();
                var reportFile = await WriteReportAsync(report, artifactDirectory, clock.GetUtcNow(), cancellationToken);
                CommandOutput.Write(output, new { reportFile, report.Totals });
                return 0;
            }
            catch (ActionTextMeasureCanceledException canceled)
            {
                var reportFile = await WriteReportAsync(canceled.Report, artifactDirectory, clock.GetUtcNow(), CancellationToken.None);
                CommandOutput.Write(output, new { reportFile, canceled.Report.Totals });
                throw;
            }
        });

    private static Func<Task<ActionTextMeasureReport>> RunBills(Func<ActionTextMeasure> create, List<CongressBillListItem> bills, long tokenCap,
        CancellationToken cancellationToken) => () => create().RunAsync(bills, tokenCap, cancellationToken);

    private static Func<Task<ActionTextMeasureReport>> RunDocuments(Func<ExecutiveSummaryMeasure> create, List<string> numbers, long tokenCap,
        CancellationToken cancellationToken) => () => create().RunAsync(numbers, tokenCap, cancellationToken);

    private static (long TokenCap, string IdsFile) ReadArguments(string[] args) =>
        args is [_, "text", "measure", var cap, var idsFile] &&
        long.TryParse(cap, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenCap) && tokenCap > 0
            ? (tokenCap, idsFile)
            : throw new ArgumentException(Usage);

    private static async Task<string> WriteReportAsync(ActionTextMeasureReport report, string artifactDirectory, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(artifactDirectory, ReportDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{now:yyyyMMddTHHmmssZ}.json");
        await using var file = new StreamWriter(path, append: false);
        CommandOutput.Write(file, report);
        return path;
    }
}
