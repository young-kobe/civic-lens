using System.Globalization;
using System.Text.Json;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;

namespace CivicLens.Host.Actions;

/// <summary>The owner command that measures the bill text prompt on a fixed list of bills. It writes a report and no database row.</summary>
public static class ActionTextMeasureCommand
{
    public const string Usage = "Use actions text measure <token-cap> <bills-file>. The token cap is a whole number of tokens. " +
        "The bills file holds a JSON array of IDs such as 119-hr-1943. Use config/action-text-measure-bills.json.";
    public const string ReportDirectory = "action-text-measure";

    public static bool Matches(string[] args) => args is ["actions", "text", ..];

    public static Task<int> ExecuteAsync(string[] args, Func<ActionTextMeasure> createMeasure, string artifactDirectory, TextWriter output,
        TimeProvider clock, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            var (tokenCap, billsFile) = ReadArguments(args);
            var bills = ReadBills(billsFile);
            var report = await createMeasure().RunAsync(bills, tokenCap, cancellationToken);
            var reportFile = await WriteReportAsync(report, artifactDirectory, clock.GetUtcNow(), cancellationToken);
            CommandOutput.Write(output, new { reportFile, report.Totals });
            return 0;
        });

    private static (long TokenCap, string BillsFile) ReadArguments(string[] args) =>
        args is ["actions", "text", "measure", var cap, var billsFile] &&
        long.TryParse(cap, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenCap) && tokenCap > 0
            ? (tokenCap, billsFile)
            : throw new ArgumentException(Usage);

    private static List<CongressBillListItem> ReadBills(string billsFile)
    {
        var ids = JsonSerializer.Deserialize<string[]>(File.ReadAllText(billsFile)) ?? throw new ArgumentException(Usage);
        if (ids.Length == 0 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("The bills file must list each bill once, and it must list at least one bill.");
        return [.. ids.Select(ReadBill)];
    }

    private static CongressBillListItem ReadBill(string id) =>
        ActionTextMeasure.TryParseId(id) ?? throw new ArgumentException($"The bill ID {id} must look like 119-hr-1943.");

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
