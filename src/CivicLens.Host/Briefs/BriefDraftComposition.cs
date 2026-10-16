using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Collection;
using CivicLens.Host.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Briefs;

internal static class BriefDraftComposition
{
    public static AnalysisComposition<BriefDraftRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts, string connectionString,
        CollectionConfiguration collection)
    {
        var status = new PostgresAnalysisStatusStore(contexts, BriefDraftTask.Task);
        var configuration = BriefDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<BriefDraftRun>.Disabled(status);
        var wakeup = PostgresBriefDraftStore.Wakeup(connectionString);
        var store = new PostgresBriefDraftStore(contexts);
        var names = collection.People.ToDictionary(person => person.Id, person => person.Name, StringComparer.Ordinal);
        var step = new BriefDraftStep(store, new AnthropicBriefDraftModel(configuration.ApiKey), configuration.Settings, names);
        var worker = new AnalysisWorker<BriefDraftRun>(step, status, TimeProvider.System, wakeup, store.EnqueueClosedWeeksAsync);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
