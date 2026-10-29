using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicLens.Host.Analysis;

internal static class DocumentChangeDraftComposition
{
    public static AnalysisComposition<AnalysisRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts,
        string connectionString, IEnumerable<string> issueIds, WorkerBeat beat, ILoggerFactory loggers)
    {
        var status = new PostgresAnalysisStatusStore(contexts);
        var configuration = DocumentChangeDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<AnalysisRun>.Disabled(status);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresDocumentChangeAnalysisStore.Table,
            DocumentChangeDraftTask.Task);
        var step = new DocumentChangeDraftStep(new PostgresDocumentChangeAnalysisStore(contexts),
            new AnthropicDocumentChangeDraftModel(configuration.ApiKey), configuration.Settings, [.. issueIds.Order(StringComparer.Ordinal)]);
        var worker = new AnalysisWorker<AnalysisRun>(step, status, TimeProvider.System, loggers.CreateLogger<AnalysisWorker<AnalysisRun>>(), wakeup, beat: beat);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
