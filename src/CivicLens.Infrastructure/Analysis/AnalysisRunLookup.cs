using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisRunLookup
{
    public static IQueryable<TRun> Latest<TRun>(CollectionAttemptDbContext db, string task, string keyProperty, string[] subjectIds)
        where TRun : class, IAnalysisRunRow
    {
        var runs = db.Set<TRun>().AsNoTracking().Where(run => run.Task == task);
        return runs.Where(run => subjectIds.Contains(EF.Property<string>(run, keyProperty)) && !runs.Any(later =>
            EF.Property<string>(later, keyProperty) == EF.Property<string>(run, keyProperty) &&
            (later.StartedAtUtcTicks > run.StartedAtUtcTicks ||
                later.StartedAtUtcTicks == run.StartedAtUtcTicks && string.Compare(later.RunId, run.RunId) > 0)));
    }
}
