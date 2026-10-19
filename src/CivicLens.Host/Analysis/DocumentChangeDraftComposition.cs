using CivicLens.Application.Analysis;
using CivicLens.Application.Collection;
using CivicLens.Application.Issues;
using CivicLens.Core.Analysis;
using CivicLens.Host.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Host.Analysis;

internal static class DocumentChangeDraftComposition
{
    public static AnalysisComposition<AnalysisRun> Create(IDbContextFactory<CollectionAttemptDbContext> contexts,
        string connectionString, CollectionConfiguration collection, IssueConfigurationRevision? issues)
    {
        var status = new PostgresAnalysisStatusStore(contexts);
        var configuration = DocumentChangeDraftConfiguration.FromEnvironment();
        if (configuration is null) return AnalysisComposition<AnalysisRun>.Disabled(status);
        var issueIds = issues?.ReadConfiguration().Issues.Select(issue => issue.Id) ?? [];
        var catalog = DocumentChangeDraftCatalog.Create(ReviewWorkspace.ReadCatalog(issueIds), ReviewOfficials.From(collection).Names);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresDocumentChangeAnalysisStore.Table,
            DocumentChangeDraftTask.Task);
        var step = new DocumentChangeDraftStep(new PostgresDocumentChangeAnalysisStore(contexts),
            new AnthropicDocumentChangeDraftModel(configuration.ApiKey), configuration.Settings, catalog);
        var worker = new AnalysisWorker<AnalysisRun>(step, status, TimeProvider.System, wakeup);
        return new(status, configuration.Settings.DailyTokenLimit, worker, wakeup);
    }
}
