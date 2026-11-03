using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Analysis;

/// <summary>Reads the refusal mark that the worker leaves on a candidate while its record is rejected. The mark is the reason of the latest run that settled
/// the candidate. Only the newest candidate that settled counts, so a later update with the same facts frees the record.</summary>
internal static class RefusedUpdates
{
    public const string Reason = "recordRejected";

    public static async Task<string[]> MarkedAsync<TRun>(CollectionAttemptDbContext db, string task, string keyProperty, string[] candidateIds,
        CancellationToken cancellationToken) where TRun : class, IAnalysisRunRow =>
        [.. (await LatestSettledAsync<TRun>(db, task, keyProperty, candidateIds, cancellationToken)).Where(run => run.Reason == Reason).Select(run => run.CandidateId)];

    public static async Task<bool> NewestSettledIsMarkedAsync<TRun>(CollectionAttemptDbContext db, string task, string keyProperty,
        IReadOnlyCollection<NewerCandidate> newer, CancellationToken cancellationToken) where TRun : class, IAnalysisRunRow
    {
        if (newer.Count == 0) return false;
        var latest = await LatestSettledAsync<TRun>(db, task, keyProperty, [.. newer.Select(candidate => candidate.CandidateId)], cancellationToken);
        var newestSettled = newer.Where(candidate => latest.Any(run => run.CandidateId == candidate.CandidateId))
            .OrderByDescending(candidate => candidate.UpdateDateUtcTicks).ThenByDescending(candidate => candidate.CreatedAtUtcTicks)
            .ThenByDescending(candidate => candidate.CandidateId, StringComparer.Ordinal).FirstOrDefault();
        return newestSettled is not null && latest.Single(run => run.CandidateId == newestSettled.CandidateId).Reason == Reason;
    }

    private static async Task<List<SettledRun>> LatestSettledAsync<TRun>(CollectionAttemptDbContext db, string task, string keyProperty, string[] candidateIds,
        CancellationToken cancellationToken) where TRun : class, IAnalysisRunRow =>
        await AnalysisRunLookup.LatestSettled<TRun>(db, task, keyProperty, candidateIds)
            .Select(run => new SettledRun(EF.Property<string>(run, keyProperty), EF.Property<string?>(run, "Reason"))).ToListAsync(cancellationToken);

    public sealed record NewerCandidate(string CandidateId, long UpdateDateUtcTicks, long CreatedAtUtcTicks);

    private sealed record SettledRun(string CandidateId, string? Reason);
}
