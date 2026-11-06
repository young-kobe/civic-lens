using System.Globalization;
using CivicLens.Application.Votes;
using CivicLens.Host.Collection;

namespace CivicLens.Host.Votes;

/// <summary>The owner command that measures the vote prompt on a fixed list of roll calls. It writes a report and no database row.</summary>
public static class VoteDecidesMeasureCommand
{
    public const string Usage = "Use votes decides measure <token-cap> <votes-file>. The token cap is a whole number of tokens. " +
        "The votes file holds a JSON array of roll call IDs such as house-119-1-100 or senate-119-2-45. Use config/vote-decides-measure-votes.json.";
    public const string ReportDirectory = "vote-decides-measure";

    public static bool Matches(string[] args) => args is ["votes", "decides", ..];

    public static Task<int> ExecuteAsync(string[] args, Func<VoteDecidesMeasure> createMeasure, string artifactDirectory, TextWriter output,
        TimeProvider clock, CancellationToken cancellationToken) =>
        OwnerCommand.RunAsync(async () =>
        {
            var (tokenCap, votesFile) = ReadArguments(args);
            var rolls = VoteRollsFile.Read(votesFile);
            try
            {
                var report = await createMeasure().RunAsync(rolls, tokenCap, cancellationToken);
                var reportFile = await WriteReportAsync(report, artifactDirectory, clock.GetUtcNow(), cancellationToken);
                CommandOutput.Write(output, new { reportFile, report.Totals });
                return 0;
            }
            catch (VoteDecidesMeasureCanceledException canceled)
            {
                var reportFile = await WriteReportAsync(canceled.Report, artifactDirectory, clock.GetUtcNow(), CancellationToken.None);
                CommandOutput.Write(output, new { reportFile, canceled.Report.Totals });
                throw;
            }
        });

    private static (long TokenCap, string VotesFile) ReadArguments(string[] args) =>
        args is ["votes", "decides", "measure", var cap, var votesFile] &&
        long.TryParse(cap, NumberStyles.None, CultureInfo.InvariantCulture, out var tokenCap) && tokenCap > 0
            ? (tokenCap, votesFile)
            : throw new ArgumentException(Usage);

    private static async Task<string> WriteReportAsync(VoteDecidesMeasureReport report, string artifactDirectory, DateTimeOffset now,
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
