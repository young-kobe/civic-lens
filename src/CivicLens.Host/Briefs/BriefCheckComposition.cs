using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Briefs;

internal static class BriefCheckComposition
{
    public static AnalysisComposition<BriefCheckRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts, BriefCheckTask.Task);
        var configuration = BriefCheckConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<BriefCheckRun>.Disabled(status);
        var wakeup = PostgresBriefCheckStore.Wakeup(connectionString);
        var step = new BriefCheckStep(new PostgresBriefCheckStore(contexts), new AnthropicBriefCheckModel(configuration.ApiKey), configuration.Settings);
        var worker = new AnalysisWorker<BriefCheckRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<BriefCheckRun>>(), beat, wakeup);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
