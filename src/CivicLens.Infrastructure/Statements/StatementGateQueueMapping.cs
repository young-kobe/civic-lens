using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal sealed class StatementGateQueueMapping : IAnalysisQueueMapping<StatementAnalysisRow, StatementGateRun>
{
    public string Table => PostgresStatementGateStore.Table;

    public string KeyColumn => "candidate_id";

    public string KeyProperty => nameof(StatementAnalysisRow.CandidateId);

    public string BudgetTable => "statement_analysis_budget";

    public string? BudgetTask => StatementGateTask.Task;

    public AnalysisTaskIdentity Identity => StatementGateTask.Identity;

    public bool CanTransition(AnalysisRecord record, AnalysisCheckpoint checkpoint) =>
        StatementGateLifecycle.CanTransition(record, checkpoint);

    public AnalysisRun RunOf(StatementGateRun run) => run.Run;

    public StatementGateRun WrapInterrupted(AnalysisRun run) => new(run, null, null, []);

    public void AddRun(CollectionAttemptDbContext db, StatementAnalysisRow row, StatementGateRun run) => db.Add(ToRow(run));

    public async Task AfterTransitionAsync(CollectionAttemptDbContext db, StatementAnalysisRow row, AnalysisCheckpoint checkpoint,
        long now, CancellationToken cancellationToken)
    {
        if (checkpoint.Status != AnalysisState.Succeeded || !StatementGateReasons.QueuesDraft(checkpoint.ErrorCode)) return;
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
            VALUES ({row.CandidateId}, {StatementDraftTask.Task}, 'Pending', 0, 0, 0, {now})
            ON CONFLICT (candidate_id, task) DO NOTHING
            """, cancellationToken);
    }

    public static StatementAnalysisRunRow ToRow(StatementGateRun gateRun)
    {
        var row = AnalysisRunRows.Create<StatementAnalysisRunRow>(gateRun.Run);
        row.IssueRevisionId = gateRun.IssueRevisionId;
        row.Reason = gateRun.Reason;
        row.QuotesJson = gateRun.Quotes.IsEmpty ? null : JsonSerializer.Serialize(gateRun.Quotes);
        return row;
    }

    public static StatementGateRun ToRun(StatementAnalysisRunRow row) => new(AnalysisRunRows.ToRun(row), row.IssueRevisionId, row.Reason,
        row.QuotesJson is null ? [] : [.. JsonSerializer.Deserialize<StatementGateQuote[]>(row.QuotesJson)!]);
}
