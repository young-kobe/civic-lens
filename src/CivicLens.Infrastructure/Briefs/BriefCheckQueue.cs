using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Core.Briefs;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Briefs;

/// <summary>Owns how a new brief revision reaches the check queue. The caller holds the brief lock, and the caller saves the context.</summary>
internal static class BriefCheckQueue
{
    private static readonly AnalysisState[] RequeuedStates =
        [AnalysisState.Succeeded, AnalysisState.Running, AnalysisState.Blocked, AnalysisState.Failed];

    private const string InsertSql = $$"""
        INSERT INTO {{PostgresBriefDraftStore.Table}} (record_id, task, week_start, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
        VALUES ({0}, {1}, {2}, 'Pending', 0, 0, 0, {{DatabaseClock.TicksSql}})
        ON CONFLICT (record_id, task) DO NOTHING
        """;

    public static PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefCheckRun> Create(
        IDbContextFactory<CollectionAttemptDbContext> contexts) => new(contexts, new BriefCheckQueueMapping());

    public static async Task QueueAsync(PostgresAnalysisQueue<BriefAnalysisRow, BriefAnalysisBudgetRow, BriefCheckRun> queue,
        CollectionAttemptDbContext db, BriefRevision revision, CancellationToken cancellationToken)
    {
        _ = await db.Database.ExecuteSqlRawAsync(InsertSql, [revision.RecordId, BriefCheckTask.Task, revision.Window.WeekStart], cancellationToken);
        _ = await queue.RequeueInAsync(db, RequeuedStates, [revision.RecordId], cancellationToken);
    }
}
