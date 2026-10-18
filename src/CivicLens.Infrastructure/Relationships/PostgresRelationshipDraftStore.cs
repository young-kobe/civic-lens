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

    private static readonly string ReadSettledFingerprintSql = $"SELECT {RelationshipStaleness.SettledFingerprint("{0}")} AS \"Value\"";

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
            (await RelationshipEvidenceReader.ReadStatementsAsync(db, [row.StatementCandidateId], cancellationToken)).SingleOrDefault(),
            isAction ? await RelationshipEvidenceReader.ReadActionAsync(db, row.TargetRecordId, cancellationToken) : null,
            isAction ? null : await RelationshipEvidenceReader.ReadVoteAsync(db, row.TargetRecordId, cancellationToken),
            settled, await RelationshipDraftRows.ReadCurrentAsync(db, pairId, cancellationToken));
    }

    public async Task<int> RefreshPairsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var found = await RelationshipPairSource.FindAllAsync(db, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await RelationshipStaleness.TakeLocksAsync(queue, db, cancellationToken);
        await RelationshipStaleness.ApplyAsync(queue, db, found, RelationshipPairScope.All, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return found.Length;
    }

    public async Task<int> CountStatementsWithoutCongressAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return RelationshipPairFinder.CountStatementsWithoutCongress(
            await RelationshipEvidenceReader.ReadStatementsAsync(db, null, cancellationToken));
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
