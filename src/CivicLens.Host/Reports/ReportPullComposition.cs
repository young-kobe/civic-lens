using CivicLens.Application;
using CivicLens.Application.Reports;
using CivicLens.Host.Collection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Reports;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Reports;

internal sealed record ReportPullComposition(AnalysisStage Stage, IWorkerWakeup Wakeup)
{
    private const string EnabledVariable = "CIVIC_LENS_REPORT_PULL";
    private const string UrlVariable = "CIVIC_LENS_REPORT_PULL_URL";
    private const string TokenVariable = "CIVIC_LENS_REPORT_PULL_TOKEN";

    public static ReportPullComposition? Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString)
    {
        var setting = Environment.GetEnvironmentVariable(EnabledVariable);
        if (string.IsNullOrWhiteSpace(setting)) return null;
        if (setting != "1") throw new ArgumentException($"{EnabledVariable} must be 1 or unset.");
        var puller = new HttpReportPuller(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, ReadAddress(), ReadToken());
        var wakeup = PostgresReportPullWakeup.FromConnectionString(connectionString);
        Action<Exception> report = exception => Console.Error.WriteLine($"reportPull failed: {exception.GetType().Name}: {exception.Message}");
        var worker = new StageWorker(new PullReaderReports(puller, new PostgresReaderReportStore(contexts)).ExecuteAsync, wakeup, report);
        return new(new AnalysisStage("reportPull", worker.ExecuteAsync), wakeup);
    }

    private static Uri ReadAddress() =>
        Uri.TryCreate(Environment.GetEnvironmentVariable(UrlVariable), UriKind.Absolute, out var address) &&
        address.Scheme is "http" or "https"
            ? address
            : throw new ArgumentException($"Report pull needs {UrlVariable} as an http or https address.");

    private static string ReadToken() =>
        Environment.GetEnvironmentVariable(TokenVariable) is { } token && token.Length >= ReaderReportProtocol.MinimumPullTokenCharacters
            ? token
            : throw new ArgumentException($"Report pull needs {TokenVariable} with at least {ReaderReportProtocol.MinimumPullTokenCharacters} characters.");
}
