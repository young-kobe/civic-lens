using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using BriefQueue = CivicLens.Infrastructure.Analysis.PostgresAnalysisQueue<
    CivicLens.Infrastructure.Briefs.Persistence.BriefAnalysisRow,
    CivicLens.Infrastructure.Briefs.Persistence.BriefAnalysisBudgetRow,
    CivicLens.Application.Briefs.BriefDraftRun>;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefStaleness
{
    private static readonly AnalysisState[] RequeuedStates = [AnalysisState.Succeeded, AnalysisState.Running];

    private static readonly string ReadStaleBriefIdsSql = $$"""
        WITH wanted AS (SELECT k.kind, k.record_id FROM unnest({0}, {1}) AS k(kind, record_id)),
        first_published AS (
            SELECT (SELECT r.published_at_utc_ticks FROM publication_releases r
                    WHERE EXISTS (SELECT 1 FROM (
                        SELECT {{ReviewQueries.ReleaseRecordColumns}} FROM jsonb_array_elements(r.records_json::jsonb) e) listed
                        WHERE lower(listed.kind) = lower(w.kind) AND listed.record_id = w.record_id)
                    ORDER BY r.release_number LIMIT 1) AS ticks
            FROM wanted w)
        SELECT b.record_id AS "Value" FROM {{PostgresBriefDraftStore.Table}} b
        WHERE b.task = {2}
          AND NOT EXISTS (SELECT 1 FROM brief_drafts d WHERE d.record_id = b.record_id AND d.review_status = 'Approved')
          AND EXISTS (SELECT 1 FROM first_published f
                      WHERE f.ticks >= {{BriefWeekSql.StartTicks("b.week_start")}} AND f.ticks < {{BriefWeekSql.EndTicks("b.week_start")}})
        """;

    public static async Task TakeLocksAsync(BriefQueue queue, CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        await queue.LockRowsAsync(db, cancellationToken);
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.BriefReview, cancellationToken);
    }

    public static Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> AfterDecision(BriefQueue queue,
        PublishedRecordKind kind) =>
        async (db, recordId, decidedRevisionNumber, cancellationToken) =>
        {
            if (await ReviewQueries.ReadActiveRevisionNumberAsync(db, kind, recordId, cancellationToken) != decidedRevisionNumber) return;
            await TakeLocksAsync(queue, db, cancellationToken);
            await RequeueWeeksOfAsync(queue, db, [(kind, recordId)], cancellationToken);
        };

    public static async Task RequeueAfterPublicationAsync(BriefQueue queue, CollectionAttemptDbContext db, PublicationCommit commit,
        CancellationToken cancellationToken)
    {
        var before = commit.BaseReleaseNumber is { } baseReleaseNumber
            ? await ReviewQueries.ReadReleaseRecordsAsync(db, baseReleaseNumber, cancellationToken) : [];
        await RequeueWeeksOfAsync(queue, db, Differing(before, commit.Records), cancellationToken);
    }

    public static async Task RequeueAfterActivationAsync(BriefQueue queue, CollectionAttemptDbContext db, int releaseNumber,
        CancellationToken cancellationToken)
    {
        var before = await ReviewQueries.ReadActiveReleaseRecordsAsync(db, cancellationToken);
        var after = await ReviewQueries.ReadReleaseRecordsAsync(db, releaseNumber, cancellationToken);
        await RequeueWeeksOfAsync(queue, db, Differing(before, after), cancellationToken);
    }

    private static (PublishedRecordKind Kind, string RecordId)[] Differing(ImmutableArray<PublishedRevisionBinding> before,
        ImmutableArray<PublishedRevisionBinding> after) =>
        [.. before.Except(after).Concat(after.Except(before)).Select(binding => (binding.Kind, binding.RecordId)).Distinct()];

    private static async Task RequeueWeeksOfAsync(BriefQueue queue, CollectionAttemptDbContext db,
        (PublishedRecordKind Kind, string RecordId)[] records, CancellationToken cancellationToken)
    {
        if (records.Length == 0) return;
        var briefIds = await db.Database.SqlQueryRaw<string>(ReadStaleBriefIdsSql, records.Select(record => record.Kind.ToString()).ToArray(),
            records.Select(record => record.RecordId).ToArray(), BriefDraftTask.Task).ToArrayAsync(cancellationToken);
        if (briefIds.Length == 0) return;
        _ = await queue.RequeueInAsync(db, RequeuedStates, briefIds, cancellationToken);
    }
}
