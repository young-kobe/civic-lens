using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Core.Analysis;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed class DocumentChangeAnalysisComposition
{
    private readonly IAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;

    private DocumentChangeAnalysisComposition(IAnalysisStatusStore status, DocumentChangeAnalysisConfiguration? configuration,
        AnalysisWorker<AnalysisRun>? worker, IWorkerWakeup? wakeup)
    {
        this.status = status;
        dailyTokenLimit = configuration?.Settings.DailyTokenLimit;
        Worker = worker;
        Wakeup = wakeup;
    }

    public AnalysisWorker<AnalysisRun>? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static DocumentChangeAnalysisComposition Create(string connectionString, CollectionConfiguration collection)
    {
        var status = PostgresAnalysisStatusStore.FromConnectionString(connectionString);
        var configuration = DocumentChangeAnalysisConfiguration.FromEnvironment();
        if (configuration is null) return new(status, null, null, null);
        var names = collection.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        var catalog = DocumentChangeAnalysisCatalog.Create(ReviewWorkspace.ReadCatalog(), names);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresDocumentChangeAnalysisStore.Table,
            DocumentChangeDraftingTask.Task);
        var step = new DocumentChangeDraftingStep(PostgresDocumentChangeAnalysisStore.FromConnectionString(connectionString),
            new AnthropicDocumentChangeDraftingModel(configuration.ApiKey), configuration.Settings, catalog);
        var worker = new AnalysisWorker<AnalysisRun>(step, status, TimeProvider.System, wakeup);
        return new(status, configuration, worker, wakeup);
    }

    public Task RecordStartAsync(CancellationToken cancellationToken) => status.RecordStartAsync(dailyTokenLimit, cancellationToken);
}
