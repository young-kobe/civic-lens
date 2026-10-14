using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisQueueCounts
{
    public static async Task<IReadOnlyDictionary<TView, int>> CountAsync<TRow, TView>(CollectionAttemptDbContext db, string task,
        Func<State, string?, TView> viewOf, CancellationToken cancellationToken)
        where TRow : class, IAnalysisRow
        where TView : struct, Enum
    {
        var groups = await db.Set<TRow>().AsNoTracking().Where(row => row.Task == task)
            .GroupBy(row => new { row.Status, row.ErrorCode }).Select(group => new { group.Key.Status, group.Key.ErrorCode, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var counts = Enum.GetValues<TView>().ToDictionary(view => view, _ => 0);
        foreach (var group in groups) counts[viewOf(group.Status, group.ErrorCode)] += group.Count;
        return counts;
    }
}
