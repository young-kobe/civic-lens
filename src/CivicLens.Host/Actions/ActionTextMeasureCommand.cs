using System.Globalization;
using CivicLens.Application.Actions;
using CivicLens.Host.Collection;

namespace CivicLens.Host.Actions;

/// <summary>The owner command that measures the bill text prompt or the bill line prompt on a fixed list of bills. It writes a report and no database row.</summary>
public static class ActionTextMeasureCommand
{
    public const string Usage = "Use actions text measure <token-cap> <bills-file> for the bill text prompt, or actions line measure <token-cap> <bills-file> " +
        "for the bill line prompt. The token cap is a whole number of tokens. The bills file holds a JSON array of IDs such as 119-hr-1943. " +
        "Use config/action-text-measure-bills.json for text, and a list of bills that have a CRS summary for lines.";
    public const string ReportDirectory = "action-text-measure";
    public const string LineReportDirectory = "action-line-measure";

    public static bool Matches(string[] args) => args is ["actions", "text" or "line", ..];

    public static Task<int> ExecuteAsync(string[] args, Func<ActionMeasuredPrompt, ActionTextMeasure> createMeasure, string artifactDirectory, TextWriter output,
        TimeProvider clock, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            var (prompt, tokenCap, billsFile) = ReadArguments(args);
            var bills = BillIdsFile.Read(billsFile);
            var directory = Path.Combine(artifactDirectory, prompt == ActionMeasuredPrompt.BillText ? ReportDirectory : LineReportDirectory);
            try
            {
                var report = await createMeasure(prompt).RunAsync(bills, tokenCap, cancellationToken);
                var reportFile = await WriteReportAsync(report, directory, clock.GetUtcNow(), cancellationToken);
                CommandOutput.Write(output, new { reportFile, report.Totals });
                return 0;
            }
            catch (ActionTextMeasureCanceledException canceled)
            {
                var reportFile = await WriteReportAsync(canceled.Report, directory, clock.GetUtcNow(), CancellationToken.None);
                CommandOutput.Write(output, new { reportFile, canceled.Report.Totals });
                throw;
            }
        });

    private static (ActionMeasuredPrompt Prompt, long TokenCap, string BillsFile) ReadArguments(string[] args) =>
        args is ["actions", var kind and ("text" or "line"), "measure", var cap, var billsFile] &&
        long.TryParse(cap, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenCap) && tokenCap > 0
            ? (kind == "text" ? ActionMeasuredPrompt.BillText : ActionMeasuredPrompt.BillLine, tokenCap, billsFile)
            : throw new ArgumentException(Usage);

    private static async Task<string> WriteReportAsync(ActionTextMeasureReport report, string directory, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{now:yyyyMMddTHHmmssZ}.json");
        await using var file = new StreamWriter(path, append: false);
        CommandOutput.Write(file, report);
        return path;
    }
}
