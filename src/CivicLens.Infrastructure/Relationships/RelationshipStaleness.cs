using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using RelationshipQueue = CivicLens.Infrastructure.Analysis.PostgresAnalysisQueue<
    CivicLens.Infrastructure.Relationships.Persistence.RelationshipAnalysisRow,
    CivicLens.Infrastructure.Relationships.Persistence.RelationshipAnalysisBudgetRow,
    CivicLens.Application.Relationships.RelationshipDraftRun>;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipStaleness
{
    private static readonly AnalysisState[] RequeuedStates = [AnalysisState.Succeeded, AnalysisState.Blocked];

    private const string InsertPairsSql = $$"""
        INSERT INTO {{PostgresRelationshipDraftStore.Table}} (pair_id, task, statement_candidate_id, target_kind, target_record_id, status, fence,
                                                             attempts, reserved_tokens, created_at_utc_ticks)
        SELECT p.pair_id, {0}, p.statement_candidate_id, p.target_kind, p.target_record_id, 'Pending', 0, 0, 0, {{DatabaseClock.TicksSql}}
        FROM unnest({1}, {2}, {3}, {4}) AS p(pair_id, statement_candidate_id, target_kind, target_record_id)
        ON CONFLICT DO NOTHING
        """;

    private static readonly string ReadStalePairIdsSql = $$"""
        SELECT a.pair_id AS "Value" FROM {{PostgresRelationshipDraftStore.Table}} a
        JOIN unnest({1}, {2}, {3}) AS f(pair_id, fingerprint, target_revision) ON f.pair_id = a.pair_id
        WHERE a.task = {0}
          AND (a.status = 'Succeeded' OR a.status = 'Blocked' AND a.error_code = '{{RelationshipDraftErrorCodes.SideNotApproved}}')
          AND NOT EXISTS (SELECT 1 FROM relationship_drafts d WHERE d.record_id = a.pair_id AND d.review_status = 'Approved')
          AND (f.fingerprint IS DISTINCT FROM {{SettledFingerprint("a.pair_id")}}
               OR {{SettledReason("a.pair_id")}} IS DISTINCT FROM '{{RelationshipDraftErrorCodes.NoRelationship}}' AND EXISTS (SELECT 1 FROM relationship_drafts d JOIN relationship_revisions r
                            ON r.record_id = d.record_id AND r.revision_number = d.current_revision_number
                          WHERE d.record_id = a.pair_id AND (r.revision_json::jsonb -> 'target' ->> 'revisionNumber')::int <> f.target_revision))
        """;

    // The shape of PostgresAnalysisQueue.lockSubjectsSql (task filter, key order, FOR UPDATE), narrowed to the pairs of one record.
    private const string LockRowsSql = $$"""
        SELECT 1 FROM {{PostgresRelationshipDraftStore.Table}} WHERE task = {0}
          AND (CAST({1} AS text) IS NULL OR statement_candidate_id = {1})
          AND (CAST({2} AS text) IS NULL OR target_kind = {2} AND target_record_id = {3})
        ORDER BY pair_id FOR UPDATE
        """;

    private const string FlagPairsSql = """
        UPDATE relationship_drafts d SET no_longer_matches = NOT (d.record_id = ANY({3}))
        FROM relationship_analysis a
        WHERE a.pair_id = d.record_id AND a.task = {0}
          AND (CAST({1} AS text) IS NULL OR a.statement_candidate_id = {1})
          AND (CAST({2} AS text) IS NULL OR a.target_kind = {2} AND a.target_record_id = {4})
          AND EXISTS (SELECT 1 FROM statement_drafts s WHERE s.candidate_id = a.statement_candidate_id AND s.review_status = 'Approved')
          AND (a.target_kind = 'Action' AND EXISTS (SELECT 1 FROM action_drafts t WHERE t.record_id = a.target_record_id AND t.review_status = 'Approved')
            OR a.target_kind = 'Vote' AND EXISTS (SELECT 1 FROM vote_drafts t WHERE t.record_id = a.target_record_id AND t.review_status = 'Approved'))
          AND d.no_longer_matches <> NOT (d.record_id = ANY({3}))
        """;

    public static string SettledFingerprint(string pairId) => LatestSettledRun(pairId, "r.context_json::jsonb ->> 'fingerprint'");

    public static string SettledReason(string pairId) => LatestSettledRun(pairId, "r.reason");

    private static string LatestSettledRun(string pairId, string column) => $"""
        (SELECT {column} FROM relationship_analysis_runs r
          WHERE r.pair_id = {pairId} AND r.task = '{RelationshipDraftTask.Task}' AND r.outcome IN ('Drafted','Bypassed')
            AND r.context_json::jsonb ->> 'fingerprint' IS NOT NULL
          ORDER BY r.finished_at_utc_ticks DESC, r.run_id DESC LIMIT 1)
        """;

    public static Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> AfterDecision(RelationshipQueue queue,
        RelationshipRecordKind kind) =>
        async (db, recordId, _, cancellationToken) =>
        {
            await db.SaveChangesAsync(cancellationToken);
            if (!await RelationshipEvidenceReader.IsApprovedAsync(db, kind, recordId, cancellationToken)) return;
            var scope = kind == RelationshipRecordKind.Statement ? RelationshipPairScope.OfStatement(recordId)
                : RelationshipPairScope.OfTarget(kind, recordId);
            await TakeLocksAsync(db, scope, cancellationToken);
            var found = await RelationshipPairSource.FindAsync(db, kind, recordId, RelationshipRevisionScope.Approved, cancellationToken);
            await ApplyAsync(queue, db, found, scope, cancellationToken);
        };

    public static async Task TakeLocksAsync(CollectionAttemptDbContext db, RelationshipPairScope scope, CancellationToken cancellationToken)
    {
        _ = await db.Database.ExecuteSqlRawAsync(LockRowsSql, [RelationshipDraftTask.Task, scope.StatementId!, scope.TargetKind?.ToString()!,
            scope.TargetId!], cancellationToken);
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.RelationshipReview, cancellationToken);
    }

    public static async Task ApplyAsync(RelationshipQueue queue, CollectionAttemptDbContext db, ImmutableArray<RelationshipPair> found,
        RelationshipPairScope scope, CancellationToken cancellationToken)
    {
        var ids = found.Select(pair => pair.PairId).ToArray();
        _ = await db.Database.ExecuteSqlRawAsync(InsertPairsSql, [RelationshipDraftTask.Task, ids,
            found.Select(pair => pair.Statement.RecordId).ToArray(), found.Select(pair => pair.Target.Kind.ToString()).ToArray(),
            found.Select(pair => pair.Target.RecordId).ToArray()], cancellationToken);
        var stale = await db.Database.SqlQueryRaw<string>(ReadStalePairIdsSql, RelationshipDraftTask.Task, ids,
            found.Select(RelationshipFingerprint.Compute).ToArray(), found.Select(pair => pair.Target.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        if (stale.Length > 0) _ = await queue.RequeueInAsync(db, RequeuedStates, stale, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlRawAsync(FlagPairsSql, [RelationshipDraftTask.Task, scope.StatementId!,
            scope.TargetKind?.ToString()!, ids, scope.TargetId!], cancellationToken);
    }
}
