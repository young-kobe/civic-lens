using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.ExecutiveActions;

/// <summary>Builds the summary worker and the summary measure. The worker is off until CIVIC_LENS_EXECUTIVE_SUMMARY_DAILY_TOKENS is set,
/// as for the other analysis tasks. The owner sets it after the paid measure.</summary>
internal static class ExecutiveSummaryComposition
{
    public static AnalysisComposition<ExecutiveSummaryRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        CodeVersion codeVersion, WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts, ExecutiveSummaryTask.Task);
        var environment = AnalysisEnvironment.Read("CIVIC_LENS_EXECUTIVE_SUMMARY_", "Executive summary");
        if (environment is null) return AnalysisComposition<ExecutiveSummaryRun>.Disabled(status);
        var settings = new ExecutiveSummarySettings(environment.DailyTokens, environment.Concurrency ?? AnalysisSettings.DefaultConcurrency);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresExecutiveSummaryStore.Table, ExecutiveSummaryTask.Task);
        var step = new ExecutiveSummaryStep(new PostgresExecutiveSummaryStore(contexts, codeVersion), new AnthropicExecutiveSummaryModel(environment.ApiKey), settings);
        return new(status, settings.DailyTokenLimit,
            new AnalysisWorker<ExecutiveSummaryRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<ExecutiveSummaryRun>>(), beat, wakeup), wakeup);
    }

    public static ExecutiveSummaryMeasure CreateMeasure(CodeVersion codeVersion)
    {
        var (connectionString, _) = OwnerCommand.ReadEnvironment();
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("The summary measure needs ANTHROPIC_API_KEY.");
        var store = new PostgresExecutiveSummaryStore(AnalysisDbContextFactory.Create(connectionString), codeVersion);
        var step = new ExecutiveSummaryStep(store, new AnthropicExecutiveSummaryModel(apiKey), new ExecutiveSummarySettings(AnalysisSettings.DefaultCallTokenLimit));
        return new(store, step);
    }
}
