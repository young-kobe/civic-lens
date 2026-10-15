using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Core.Analysis;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Analysis;

internal static class DocumentChangeDraftComposition
{
    public static AnalysisComposition<AnalysisRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts,
        string connectionString, CollectionConfiguration collection)
    {
        var status = new PostgresAnalysisStatusStore(contexts);
        var configuration = DocumentChangeDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<AnalysisRun>.Disabled(status);
        var names = collection.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        var catalog = DocumentChangeAnalysisCatalog.Create(ReviewWorkspace.ReadCatalog(), names);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresDocumentChangeAnalysisStore.Table,
            DocumentChangeDraftTask.Task);
        var step = new DocumentChangeDraftStep(new PostgresDocumentChangeAnalysisStore(contexts),
            new AnthropicDocumentChangeDraftModel(configuration.ApiKey), configuration.Settings, catalog);
        var worker = new AnalysisWorker<AnalysisRun>(step, status, TimeProvider.System, wakeup);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
