using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisPriority
{
    // The shape of PostgresAnalysisQueue.lockSubjectsSql: task filter, key order, FOR UPDATE. A running row keeps its priority.
    public static async Task RaiseAsync(CollectionAttemptDbContext db, string table, string keyColumn, string task, string[] subjectIds,
        int priority, CancellationToken cancellationToken)
    {
        if (subjectIds.Length == 0) return;
        var sql = $$"""
            UPDATE {{table}} t SET priority = {0}
            FROM (SELECT {{keyColumn}} AS subject_id FROM {{table}}
                  WHERE task = {1} AND {{keyColumn}} = ANY({2}) AND status <> 'Running' AND priority > {0}
                  ORDER BY {{keyColumn}} FOR UPDATE) locked
            WHERE t.{{keyColumn}} = locked.subject_id AND t.task = {1}
            """;
        _ = await db.Database.ExecuteSqlRawAsync(sql, [priority, task, subjectIds], cancellationToken);
    }
}
