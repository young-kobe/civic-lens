using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Statements;

namespace CivicLens.Host.Statements;

internal sealed class StatementGateComposition
{
    public const string IssuesUnsetReason = "issuesConfigurationUnset";
    private readonly IDocumentChangeAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;
    private readonly bool issuesUnset;

    private StatementGateComposition(IDocumentChangeAnalysisStatusStore status, StatementGateConfiguration? configuration,
        bool issuesUnset, StatementGateWorker? worker, IWorkerWakeup? wakeup)
    {
        this.status = status;
        dailyTokenLimit = configuration?.Settings.DailyTokenLimit;
        this.issuesUnset = issuesUnset;
        Worker = worker;
        Wakeup = wakeup;
    }

    public StatementGateWorker? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static StatementGateComposition Create(string connectionString)
    {
        var status = PostgresDocumentChangeAnalysisStatusStore.FromConnectionString(connectionString, StatementGateTask.Task);
        var configuration = StatementGateConfiguration.FromEnvironment();
        if (configuration is null) return new(status, null, false, null, null);
        var rubric = StatementIssueRubric.FromEnvironment();
        if (rubric is null) return new(status, configuration, true, null, null);
        var wakeup = PostgresStatementAnalysisWakeup.FromConnectionString(connectionString);
        var worker = new StatementGateWorker(PostgresStatementAnalysisStore.FromConnectionString(connectionString), status,
            new AnthropicStatementGateModel(configuration.ApiKey), configuration.Settings, rubric, TimeProvider.System, wakeup);
        return new(status, configuration, false, worker, wakeup);
    }

    public async Task RecordStartAsync(CancellationToken cancellationToken)
    {
        await status.RecordStartAsync(dailyTokenLimit, cancellationToken);
        if (issuesUnset) await status.RecordPauseAsync(IssuesUnsetReason, null, cancellationToken);
    }
}
