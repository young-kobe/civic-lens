using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisPriority
{
    public static Task RaiseAsync(CollectionAttemptDbContext db, string table, string keyColumn, string task, string[] subjectIds, int priority,
        CancellationToken cancellationToken) => WriteAsync(db, table, keyColumn, task, subjectIds, priority, ">", cancellationToken);

    public static Task SetAsync(CollectionAttemptDbContext db, string table, string keyColumn, string task, string[] subjectIds, int priority,
        CancellationToken cancellationToken) => WriteAsync(db, table, keyColumn, task, subjectIds, priority, "<>", cancellationToken);

    // The shape of PostgresAnalysisQueue.lockSubjectsSql: task filter, key order, FOR UPDATE. A running row keeps its priority.
    private static async Task WriteAsync(CollectionAttemptDbContext db, string table, string keyColumn, string task, string[] subjectIds,
        int priority, string comparison, CancellationToken cancellationToken)
    {
        if (subjectIds.Length == 0) return;
        var sql = $$"""
            UPDATE {{table}} t SET priority = {0}
            FROM (SELECT {{keyColumn}} AS subject_id FROM {{table}}
                  WHERE task = {1} AND {{keyColumn}} = ANY({2}) AND status <> 'Running' AND priority {{comparison}} {0}
                  ORDER BY {{keyColumn}} FOR UPDATE) locked
            WHERE t.{{keyColumn}} = locked.subject_id AND t.task = {1}
            """;
        _ = await db.Database.ExecuteSqlRawAsync(sql, [priority, task, subjectIds], cancellationToken);
    }
}
