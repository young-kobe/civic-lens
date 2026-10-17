using System.Data;
using System.Linq.Expressions;
using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Relationships.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Relationships;

public sealed class PostgresRelationshipDraftStore : PostgresAnalysisStore<RelationshipDraftRun>, IRelationshipDraftStore
{
    public const string Table = "relationship_analysis";

    private const string InsertPairsSql = $$"""
        INSERT INTO {{Table}} (pair_id, task, statement_candidate_id, target_kind, target_record_id, status, fence, attempts, reserved_tokens,
                               created_at_utc_ticks)
        SELECT p.pair_id, {0}, p.statement_candidate_id, p.target_kind, p.target_record_id, 'Pending', 0, 0, 0, {{DatabaseClock.TicksSql}}
        FROM unnest({1}, {2}, {3}, {4}) AS p(pair_id, statement_candidate_id, target_kind, target_record_id)
        ON CONFLICT DO NOTHING
        """;

    private static readonly string ReadStalePairIdsSql = $$"""
        SELECT a.pair_id AS "Value" FROM {{Table}} a
        JOIN unnest({1}, {2}) AS f(pair_id, fingerprint) ON f.pair_id = a.pair_id
        WHERE a.task = {0}
          AND (a.status = 'Succeeded' OR a.status = 'Blocked' AND a.error_code = '{{RelationshipDraftErrorCodes.SideNotApproved}}')
          AND NOT EXISTS (SELECT 1 FROM relationship_drafts d WHERE d.record_id = a.pair_id AND d.review_status = 'Approved')
          AND f.fingerprint IS DISTINCT FROM {{SettledFingerprint("a.pair_id")}}
        """;

    private const string FlagPairsNoLongerFoundSql = """
        UPDATE relationship_drafts SET no_longer_matches = NOT (record_id = ANY({0}))
        WHERE no_longer_matches <> NOT (record_id = ANY({0}))
        """;

    private static readonly string ReadSettledFingerprintSql = $"SELECT {SettledFingerprint("{0}")} AS \"Value\"";

    private static readonly State[] RequeuedStates = [State.Succeeded, State.Blocked];

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<RelationshipAnalysisRow, RelationshipAnalysisBudgetRow, RelationshipDraftRun> queue;

    public PostgresRelationshipDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new RelationshipDraftQueueMapping())) { }

    private PostgresRelationshipDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<RelationshipAnalysisRow, RelationshipAnalysisBudgetRow, RelationshipDraftRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

    public async Task<RelationshipDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<RelationshipAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == RelationshipDraftTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason);
    }

    public async Task<RelationshipDraftEvidence?> ReadEvidenceAsync(string pairId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<RelationshipAnalysisRow>().AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.PairId == pairId && entry.Task == RelationshipDraftTask.Task, cancellationToken);
        if (row is null) return null;
        var isAction = row.TargetKind == RelationshipRecordKind.Action;
        var settled = await db.Database.SqlQueryRaw<string>(ReadSettledFingerprintSql, pairId).SingleOrDefaultAsync(cancellationToken);
        return new(pairId, row.TargetKind,
            (await RelationshipEvidenceReader.ReadStatementsAsync(db, row.StatementCandidateId, cancellationToken)).SingleOrDefault(),
            isAction ? await RelationshipEvidenceReader.ReadActionAsync(db, row.TargetRecordId, cancellationToken) : null,
            isAction ? null : await RelationshipEvidenceReader.ReadVoteAsync(db, row.TargetRecordId, cancellationToken),
            settled, await RelationshipDraftRows.ReadCurrentAsync(db, pairId, cancellationToken));
    }

    public async Task RefreshPairsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var found = (await FindPairsAsync(db, cancellationToken)).Found;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await queue.LockRowsAsync(db, cancellationToken);
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.RelationshipReview, cancellationToken);
        var ids = found.Select(pair => pair.PairId).ToArray();
        _ = await db.Database.ExecuteSqlRawAsync(InsertPairsSql, [RelationshipDraftTask.Task, ids,
            found.Select(pair => pair.Statement.RecordId).ToArray(), found.Select(pair => pair.Target.Kind.ToString()).ToArray(),
            found.Select(pair => pair.Target.RecordId).ToArray()], cancellationToken);
        var stale = await db.Database.SqlQueryRaw<string>(ReadStalePairIdsSql, RelationshipDraftTask.Task, ids,
            found.Select(RelationshipFingerprint.Compute).ToArray()).ToArrayAsync(cancellationToken);
        if (stale.Length > 0) _ = await queue.RequeueInAsync(db, RequeuedStates, stale, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        _ = await db.Database.ExecuteSqlRawAsync(FlagPairsNoLongerFoundSql, [ids], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountStatementsSkippedWithoutDateAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return (await FindPairsAsync(db, cancellationToken)).StatementsSkippedWithoutDate;
    }

    public async Task<IReadOnlyList<RelationshipDraftListItem>> ListAsync(RelationshipDraftView? view, int limit,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entries = db.Set<RelationshipAnalysisRow>().AsNoTracking().Where(row => row.Task == RelationshipDraftTask.Task);
        if (view is { } selected) entries = entries.Where(InView(selected));
        var rows = await entries.OrderBy(entry => entry.CreatedAtUtcTicks).ThenBy(entry => entry.PairId).Take(limit).ToArrayAsync(cancellationToken);
        var latest = await AnalysisRunLookup.Latest<RelationshipAnalysisRunRow>(db, RelationshipDraftTask.Task,
                nameof(RelationshipAnalysisRunRow.PairId), [.. rows.Select(row => row.PairId)])
            .Select(run => new { run.PairId, run.RunId }).ToDictionaryAsync(run => run.PairId, run => run.RunId, cancellationToken);
        return [.. rows.Select(row => new RelationshipDraftListItem(row.PairId, row.StatementCandidateId, row.TargetKind, row.TargetRecordId,
            row.Status, ViewOf(row.Status, row.ErrorCode), row.ErrorCode, row.Attempts, latest.GetValueOrDefault(row.PairId)))];
    }

    public async Task<IReadOnlyDictionary<RelationshipDraftView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AnalysisQueueCounts.CountAsync<RelationshipAnalysisRow, RelationshipDraftView>(db, RelationshipDraftTask.Task, ViewOf,
            cancellationToken);
    }

    private static string SettledFingerprint(string pairId) => $"""
        (SELECT r.context_json::jsonb ->> 'fingerprint' FROM relationship_analysis_runs r
          WHERE r.pair_id = {pairId} AND r.task = '{RelationshipDraftTask.Task}' AND r.outcome IN ('Drafted','Bypassed') AND r.context_json::jsonb ->> 'fingerprint' IS NOT NULL
          ORDER BY r.finished_at_utc_ticks DESC, r.run_id DESC LIMIT 1)
        """;

    private static async Task<RelationshipPairs> FindPairsAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var statements = await RelationshipEvidenceReader.ReadStatementsAsync(db, null, cancellationToken);
        if (statements.IsEmpty) return new([], 0);
        var officialIds = statements.SelectMany(statement => statement.Revision.SpeakerOfficialIds).Distinct(StringComparer.Ordinal).ToArray();
        var actions = await RelationshipEvidenceReader.ReadActionsSponsoredByAsync(db, officialIds, cancellationToken);
        var votes = await RelationshipEvidenceReader.ReadVotesOnAsync(db, RelationshipPairFinder.BillsMentionedIn(statements), cancellationToken);
        return RelationshipPairFinder.Find(statements, actions, votes);
    }

    private static Expression<Func<RelationshipAnalysisRow, bool>> InView(RelationshipDraftView view) => view switch
    {
        RelationshipDraftView.Pending => AnalysisQueueStates.IsPending<RelationshipAnalysisRow>(),
        RelationshipDraftView.Drafted => row => row.Status == State.Succeeded && row.ErrorCode == null,
        RelationshipDraftView.Unchanged => row => row.Status == State.Succeeded && row.ErrorCode == RelationshipDraftErrorCodes.Unchanged,
        RelationshipDraftView.NoRelationship => row => row.Status == State.Succeeded && row.ErrorCode == RelationshipDraftErrorCodes.NoRelationship,
        RelationshipDraftView.NoLongerMatches => row => row.Status == State.Succeeded && row.ErrorCode == RelationshipDraftErrorCodes.NoLongerMatches,
        _ => AnalysisQueueStates.IsFailed<RelationshipAnalysisRow>()
    };

    private static RelationshipDraftView ViewOf(State status, string? errorCode) => status switch
    {
        State.Succeeded => errorCode switch
        {
            null => RelationshipDraftView.Drafted,
            RelationshipDraftErrorCodes.Unchanged => RelationshipDraftView.Unchanged,
            RelationshipDraftErrorCodes.NoRelationship => RelationshipDraftView.NoRelationship,
            _ => RelationshipDraftView.NoLongerMatches
        },
        var failed when AnalysisQueueStates.IsFailed(failed) => RelationshipDraftView.Failed,
        _ => RelationshipDraftView.Pending
    };
}
