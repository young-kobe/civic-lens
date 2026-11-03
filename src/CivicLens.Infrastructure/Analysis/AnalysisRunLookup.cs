using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

internal static class AnalysisRunLookup
{
    private static readonly AnalysisRunOutcome[] SettledOutcomes = [AnalysisRunOutcome.Drafted, AnalysisRunOutcome.Bypassed, AnalysisRunOutcome.DraftDiscarded];

    /// <summary>The latest run among those that settled the subject. A failed or interrupted run does not hide the answer before it.</summary>
    public static IQueryable<TRun> LatestSettled<TRun>(CollectionAttemptDbContext db, string task, string keyProperty, string[] subjectIds)
        where TRun : class, IAnalysisRunRow => Latest<TRun>(db, task, keyProperty, subjectIds, SettledOutcomes);

    public static IQueryable<TRun> Latest<TRun>(CollectionAttemptDbContext db, string task, string keyProperty, string[] subjectIds,
        AnalysisRunOutcome[]? outcomes = null)
        where TRun : class, IAnalysisRunRow
    {
        var runs = db.Set<TRun>().AsNoTracking().Where(run => run.Task == task && (outcomes == null || outcomes.Contains(run.Outcome)));
        return runs.Where(run => subjectIds.Contains(EF.Property<string>(run, keyProperty)) && !runs.Any(later =>
            EF.Property<string>(later, keyProperty) == EF.Property<string>(run, keyProperty) &&
            (later.StartedAtUtcTicks > run.StartedAtUtcTicks ||
                later.StartedAtUtcTicks == run.StartedAtUtcTicks && string.Compare(later.RunId, run.RunId) > 0)));
    }
}
