using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Analysis;

namespace CivicLens.Host.Analysis;

internal sealed class DocumentChangeAnalysisComposition
{
    private readonly IDocumentChangeAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;

    private DocumentChangeAnalysisComposition(IDocumentChangeAnalysisStatusStore status, DocumentChangeAnalysisConfiguration? configuration,
        DocumentChangeAnalysisWorker? worker, IWorkerWakeup? wakeup)
    {
        this.status = status;
        dailyTokenLimit = configuration?.Settings.DailyTokenLimit;
        Worker = worker;
        Wakeup = wakeup;
    }

    public DocumentChangeAnalysisWorker? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static DocumentChangeAnalysisComposition Create(string connectionString, CollectionConfiguration collection)
    {
        var status = PostgresDocumentChangeAnalysisStatusStore.FromConnectionString(connectionString);
        var configuration = DocumentChangeAnalysisConfiguration.FromEnvironment();
        if (configuration is null) return new(status, null, null, null);
        var names = collection.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        var catalog = DocumentChangeAnalysisCatalog.Create(ReviewWorkspace.ReadCatalog(), names);
        var wakeup = PostgresDocumentChangeAnalysisWakeup.FromConnectionString(connectionString);
        var worker = new DocumentChangeAnalysisWorker(PostgresDocumentChangeAnalysisStore.FromConnectionString(connectionString), status,
            new AnthropicDocumentChangeDraftingModel(configuration.ApiKey), configuration.Settings, catalog, TimeProvider.System, wakeup);
        return new(status, configuration, worker, wakeup);
    }

    public Task RecordStartAsync(CancellationToken cancellationToken) => status.RecordStartAsync(dailyTokenLimit, cancellationToken);
}
