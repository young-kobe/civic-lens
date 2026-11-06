using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

internal static class StaleAnalysisRuns
{
    public static async Task<HashSet<string>> FindStaleRunIdsAsync<TRun>(CollectionAttemptDbContext db, string[] runIds,
        AnalysisCurrentVersions current, CancellationToken cancellationToken)
        where TRun : class, IAnalysisRunRow
    {
        var versions = await db.Set<TRun>().AsNoTracking().Where(run => runIds.Contains(run.RunId))
            .Select(run => new { run.RunId, run.TaskVersion, run.PromptVersion, run.SchemaVersion }).ToArrayAsync(cancellationToken);
        return versions.Where(run => !current.IsCurrent(run.TaskVersion, run.PromptVersion, run.SchemaVersion))
            .Select(run => run.RunId).ToHashSet(StringComparer.Ordinal);
    }
}
