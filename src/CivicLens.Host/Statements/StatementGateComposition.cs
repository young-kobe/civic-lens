using CivicLens.Application;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Statements;

namespace CivicLens.Host.Statements;

internal sealed class StatementGateComposition
{
    public const string IssuesUnsetReason = "issuesConfigurationUnset";
    private readonly IAnalysisStatusStore status;
    private readonly long? dailyTokenLimit;
    private readonly bool issuesUnset;

    private StatementGateComposition(IAnalysisStatusStore status, StatementGateConfiguration? configuration,
        bool issuesUnset, AnalysisWorker<StatementGateRun>? worker, IWorkerWakeup? wakeup)
    {
        this.status = status;
        dailyTokenLimit = configuration?.Settings.DailyTokenLimit;
        this.issuesUnset = issuesUnset;
        Worker = worker;
        Wakeup = wakeup;
    }

    public AnalysisWorker<StatementGateRun>? Worker { get; }
    public IWorkerWakeup? Wakeup { get; }

    public static StatementGateComposition Create(string connectionString)
    {
        var status = PostgresAnalysisStatusStore.FromConnectionString(connectionString, StatementGateTask.Task);
        var configuration = StatementGateConfiguration.FromEnvironment();
        if (configuration is null) return new(status, null, false, null, null);
        var rubric = StatementIssueRubric.FromEnvironment();
        if (rubric is null) return new(status, configuration, true, null, null);
        var wakeup = PostgresAnalysisWakeup.FromConnectionString(connectionString, PostgresStatementAnalysisStore.Table,
            StatementGateTask.Task);
        var step = new StatementGateStep(PostgresStatementAnalysisStore.FromConnectionString(connectionString),
            new AnthropicStatementGateModel(configuration.ApiKey), configuration.Settings, rubric);
        var worker = new AnalysisWorker<StatementGateRun>(step, status, TimeProvider.System, wakeup);
        return new(status, configuration, false, worker, wakeup);
    }

    public async Task RecordStartAsync(CancellationToken cancellationToken)
    {
        await status.RecordStartAsync(dailyTokenLimit, cancellationToken);
        if (issuesUnset) await status.RecordPauseAsync(IssuesUnsetReason, null, cancellationToken);
    }
}
