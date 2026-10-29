using CivicLens.Core.Collection;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal static class PostgresStatementCandidateRegistration
{
    public sealed record KnownFeedCandidates(HashSet<string> Links, int RegisteredForAttempt);

    public const string GateTask = "gate";

    public static async Task RegisterAsync(CollectionAttemptDbContext db, CapturedAttemptResult attempt,
        string extractionId, string articleText, CancellationToken cancellationToken)
    {
        if (!await CameFromDiscoveryAdmissionAsync(db, attempt.AttemptId, cancellationToken)) return;
        _ = await InsertAsync(db, attempt, attempt.RequestedUrl, extractionId, articleText, cancellationToken);
    }

    public static Task<bool> RegisterFeedItemAsync(CollectionAttemptDbContext db, CapturedAttemptResult feed, string link,
        string extractionId, string articleText, CancellationToken cancellationToken) =>
        InsertAsync(db, feed, link, extractionId, articleText, cancellationToken);

    public static async Task<KnownFeedCandidates> ReadKnownAsync(CollectionAttemptDbContext db, CapturedAttemptResult feed,
        IEnumerable<string> links, CancellationToken cancellationToken)
    {
        var linkById = links.Distinct(StringComparer.Ordinal)
            .ToDictionary(link => PostgresCollectionJobStore.HashCandidate(feed.SourceId, link), StringComparer.Ordinal);
        var ids = linkById.Keys.ToArray();
        var rows = db.Set<StatementCandidateRow>().AsNoTracking();
        var known = await rows.Where(row => ids.Contains(row.CandidateId)).Select(row => row.CandidateId)
            .ToArrayAsync(cancellationToken);
        var registered = await rows.CountAsync(row => row.AttemptId == feed.AttemptId, cancellationToken);
        return new KnownFeedCandidates([.. known.Select(id => linkById[id])], registered);
    }

    private static async Task<bool> InsertAsync(CollectionAttemptDbContext db, CapturedAttemptResult attempt, string url,
        string extractionId, string articleText, CancellationToken cancellationToken)
    {
        var candidateId = PostgresCollectionJobStore.HashCandidate(attempt.SourceId, url);
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
            VALUES ({candidateId}, {attempt.SourceId}, {url}, {extractionId}, {attempt.AttemptId}, {now})
            ON CONFLICT (candidate_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 0) return false;
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks, priority)
            VALUES ({candidateId}, {GateTask}, 'Pending', 0, 0, 0, {now}, {ReviewPriority.Highest})
            """, cancellationToken);
        await StatementBillMentions.RaiseRowsNamedByAsync(db, DateOnly.FromDateTime(attempt.ObservedAt.UtcDateTime), articleText,
            cancellationToken);
        return true;
    }

    private static Task<bool> CameFromDiscoveryAdmissionAsync(CollectionAttemptDbContext db, string attemptId,
        CancellationToken cancellationToken) =>
        (from jobAttempt in db.Set<JobAttemptRow>()
         join job in db.Set<JobRow>() on jobAttempt.JobId equals job.JobId
         where jobAttempt.AttemptId == attemptId &&
               job.IdempotencyKey.StartsWith(PostgresCollectionJobStore.DiscoveryAdmissionKeyPrefix)
         select job.JobId).AnyAsync(cancellationToken);
}
