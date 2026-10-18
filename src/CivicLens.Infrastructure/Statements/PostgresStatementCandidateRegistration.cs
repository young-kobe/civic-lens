using CivicLens.Core.Collection;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal static class PostgresStatementCandidateRegistration
{
    public const string GateTask = "gate";

    public static async Task RegisterAsync(CollectionAttemptDbContext db, CapturedAttemptResult attempt,
        string extractionId, string articleText, CancellationToken cancellationToken)
    {
        if (!await CameFromDiscoveryAdmissionAsync(db, attempt.AttemptId, cancellationToken)) return;
        var candidateId = PostgresCollectionJobStore.HashCandidate(attempt.SourceId, attempt.RequestedUrl);
        var now = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
            VALUES ({candidateId}, {attempt.SourceId}, {attempt.RequestedUrl}, {extractionId}, {attempt.AttemptId}, {now})
            ON CONFLICT (candidate_id) DO NOTHING
            """, cancellationToken);
        if (inserted == 0) return;
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks, priority)
            VALUES ({candidateId}, {GateTask}, 'Pending', 0, 0, 0, {now}, {ReviewPriority.Highest})
            """, cancellationToken);
        await StatementBillMentions.RaiseRowsNamedByAsync(db, DateOnly.FromDateTime(attempt.ObservedAt.UtcDateTime), articleText,
            cancellationToken);
    }

    private static Task<bool> CameFromDiscoveryAdmissionAsync(CollectionAttemptDbContext db, string attemptId,
        CancellationToken cancellationToken) =>
        (from jobAttempt in db.Set<JobAttemptRow>()
         join job in db.Set<JobRow>() on jobAttempt.JobId equals job.JobId
         where jobAttempt.AttemptId == attemptId &&
               job.IdempotencyKey.StartsWith(PostgresCollectionJobStore.DiscoveryAdmissionKeyPrefix)
         select job.JobId).AnyAsync(cancellationToken);
}
