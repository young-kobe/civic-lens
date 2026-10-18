using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Relationships;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Relationships;

internal static class RelationshipDraftComposition
{
    public static AnalysisComposition<RelationshipDraftRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts,
        string connectionString, CollectionConfiguration collection)
    {
        var status = new PostgresAnalysisStatusStore(contexts, RelationshipDraftTask.Task);
        var configuration = RelationshipDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<RelationshipDraftRun>.Disabled(status);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresRelationshipDraftStore.Table, RelationshipDraftTask.Task);
        var store = new PostgresRelationshipDraftStore(contexts);
        var names = collection.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        var step = new RelationshipDraftStep(store, new AnthropicRelationshipDraftModel(configuration.ApiKey), configuration.Settings, names);
        var worker = new AnalysisWorker<RelationshipDraftRun>(step, status, TimeProvider.System, wakeup);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
