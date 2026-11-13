using CivicLens.Application.Analysis;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.ExecutiveActions.Persistence;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal static class ExecutiveSummaryQueueRows
{
    /// <summary>The queue entry of a record. The store that saves the text row adds it in the same transaction, so a text always has its entry.</summary>
    public static void Add(CollectionAttemptDbContext db, string recordId, long nowTicks) => db.Add(new ExecutiveSummaryAnalysisRow
    {
        RecordId = recordId,
        Task = ExecutiveSummaryTask.Task,
        Status = AnalysisState.Pending,
        CreatedAtUtcTicks = nowTicks,
        Priority = ExecutiveSummaryTask.QueuePriority
    });
}
