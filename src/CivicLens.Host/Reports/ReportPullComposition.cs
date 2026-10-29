using CivicLens.Application;
using CivicLens.Application.Reports;
using CivicLens.Host.Collection;
using CivicLens.Infrastructure;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Reports;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Reports;

internal sealed record ReportPullComposition(AnalysisStage Stage, IWorkerWakeup Wakeup)
{
    private const string EnabledVariable = "CIVIC_LENS_REPORT_PULL";
    private const string UrlVariable = "CIVIC_LENS_REPORT_PULL_URL";
    private const string TokenVariable = "CIVIC_LENS_REPORT_PULL_TOKEN";

    public static ReportPullComposition? Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        WorkerBeat beat, ILoggerFactory loggers)
    {
        var setting = Environment.GetEnvironmentVariable(EnabledVariable);
        if (string.IsNullOrWhiteSpace(setting)) return null;
        if (setting != "1") throw new ArgumentException($"{EnabledVariable} must be 1 or unset.");
        var client = new HttpClient
        {
            MaxResponseContentBufferSize = HttpReportPuller.MaximumReplyBytes,
            Timeout = Timeout.InfiniteTimeSpan
        };
        var puller = new HttpReportPuller(client, ReadAddress(), ReadToken());
        var wakeup = PostgresIntervalWakeup.FromConnectionString(connectionString, "civic_lens_report_pull", TimeSpan.FromMinutes(5));
        var worker = new StageWorker(WorkerLoopNames.ReportPull, new PullReaderReports(puller, new PostgresReaderReportStore(contexts), loggers.CreateLogger<PullReaderReports>()).ExecuteAsync, wakeup,
            loggers.CreateLogger<StageWorker>(), beat);
        return new(new AnalysisStage(WorkerLoopNames.ReportPull, worker.ExecuteAsync), wakeup);
    }

    private static Uri ReadAddress() =>
        Uri.TryCreate(Environment.GetEnvironmentVariable(UrlVariable), UriKind.Absolute, out var address) &&
        address.Scheme is "http" or "https"
            ? address
            : throw new ArgumentException($"Report pull needs {UrlVariable} as an http or https address.");

    private static string ReadToken() =>
        Environment.GetEnvironmentVariable(TokenVariable) is { } token && ReaderReportProtocol.IsPullToken(token)
            ? token
            : throw new ArgumentException($"Report pull needs {TokenVariable} with at least {ReaderReportProtocol.MinimumPullTokenCharacters} characters.");
}
