using System.Linq.Expressions;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementAnalysisStore : PostgresAnalysisStore<StatementGateRun>, IStatementAnalysisStore
{
    public const string Table = "statement_analysis";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<StatementAnalysisRow, StatementAnalysisBudgetRow, StatementGateRun> queue;

    public PostgresStatementAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new StatementAnalysisQueueMapping())) { }

    private PostgresStatementAnalysisStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<StatementAnalysisRow, StatementAnalysisBudgetRow, StatementGateRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

    public async Task<StatementGateRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == StatementGateTask.Task, cancellationToken);
        return row is null ? null : StatementAnalysisQueueMapping.ToRun(row);
    }

    public async Task<StatementGateEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ReadArticleAsync(db, candidateId, cancellationToken);
    }

    internal static async Task<StatementGateEvidence?> ReadArticleAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken)
    {
        var extractionId = await db.Set<StatementCandidateRow>().AsNoTracking().Where(row => row.CandidateId == candidateId)
            .Select(row => row.ExtractionId).SingleOrDefaultAsync(cancellationToken);
        if (extractionId is null) return null;
        var extraction = await PostgresDocumentExtractionStore.ReadAsync(db, extractionId, cancellationToken);
        return extraction is null ? null : new(candidateId, extraction);
    }

    public Task<int> RegateAsync(string issueRevisionId, DateOnly? since, CancellationToken cancellationToken) =>
        queue.RequeueAsync(State.Succeeded, StatementGateReasons.NoTrackedIssue, null, async (db, skipped, token) =>
        {
            var ids = skipped.Select(row => row.CandidateId).ToArray();
            var boundToOlder = await CandidatesBoundToOlderRevisionAsync(db, ids, issueRevisionId, token);
            var capturedSince = since is null ? null : await CandidatesCapturedSinceAsync(db, ids, since.Value, token);
            return skipped.Where(row => boundToOlder.Contains(row.CandidateId) &&
                (capturedSince is null || capturedSince.Contains(row.CandidateId)));
        }, cancellationToken);

    public async Task<IReadOnlyList<StatementGateListItem>> ListAsync(StatementGateView? view, int limit, CancellationToken cancellationToken)
    {
        var entries = await StatementAnalysisListing.ListAsync(contextFactory, StatementGateTask.Task,
            view is { } selected ? InView(selected) : null, limit, cancellationToken);
        return [.. entries.Select(entry => new StatementGateListItem(entry.Row.CandidateId, entry.SourceId, entry.Url, entry.Row.Status,
            ViewOf(entry.Row.Status, entry.Row.ErrorCode), entry.Row.ErrorCode, entry.Row.Attempts, entry.LatestRunId,
            entry.LatestIssueRevisionId))];
    }

    public async Task<IReadOnlyDictionary<StatementGateView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AnalysisQueueCounts.CountAsync<StatementAnalysisRow, StatementGateView>(db, StatementGateTask.Task, ViewOf, cancellationToken);
    }

    private static async Task<HashSet<string>> CandidatesBoundToOlderRevisionAsync(CollectionAttemptDbContext db, string[] candidateIds,
        string issueRevisionId, CancellationToken cancellationToken)
    {
        var runs = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .Where(run => run.Task == StatementGateTask.Task && candidateIds.Contains(run.CandidateId))
            .Select(run => new { run.CandidateId, run.IssueRevisionId, run.StartedAtUtcTicks }).ToArrayAsync(cancellationToken);
        return runs.GroupBy(run => run.CandidateId)
            .Where(group => group.OrderByDescending(run => run.StartedAtUtcTicks).First().IssueRevisionId != issueRevisionId)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> CandidatesCapturedSinceAsync(CollectionAttemptDbContext db, string[] candidateIds,
        DateOnly since, CancellationToken cancellationToken)
    {
        var sinceTicks = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
        var captured = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                              join attempt in db.Attempts.AsNoTracking() on candidate.AttemptId equals attempt.AttemptId
                              where candidateIds.Contains(candidate.CandidateId) && attempt.ObservedAtUtcTicks >= sinceTicks
                              select candidate.CandidateId).ToArrayAsync(cancellationToken);
        return captured.ToHashSet(StringComparer.Ordinal);
    }

    private static Expression<Func<StatementAnalysisRow, bool>> InView(StatementGateView view) => view switch
    {
        StatementGateView.Pending => AnalysisQueueStates.IsPending<StatementAnalysisRow>(),
        StatementGateView.Passed => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.Passed,
        StatementGateView.Skipped => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.NoTrackedIssue,
        StatementGateView.FailedOpen => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.FailedOpen,
        StatementGateView.OverCap => row => row.Status == State.Succeeded && row.ErrorCode == StatementGateReasons.OverByteCap,
        _ => AnalysisQueueStates.IsFailed<StatementAnalysisRow>()
    };

    private static StatementGateView ViewOf(State status, string? errorCode) => status switch
    {
        State.Succeeded => errorCode switch
        {
            StatementGateReasons.Passed => StatementGateView.Passed,
            StatementGateReasons.NoTrackedIssue => StatementGateView.Skipped,
            StatementGateReasons.FailedOpen => StatementGateView.FailedOpen,
            _ => StatementGateView.OverCap
        },
        var failed when AnalysisQueueStates.IsFailed(failed) => StatementGateView.Failed,
        _ => StatementGateView.Pending
    };
}
