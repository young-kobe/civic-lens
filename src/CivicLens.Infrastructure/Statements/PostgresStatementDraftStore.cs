using System.Linq.Expressions;
using CivicLens.Application.Collection;
using CivicLens.Application.Statements;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementDraftStore : PostgresAnalysisStore<StatementDraftRun>, IStatementDraftStore
{
    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;

    public PostgresStatementDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new StatementDraftQueueMapping())) { }

    private PostgresStatementDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<StatementAnalysisRow, StatementAnalysisBudgetRow, StatementDraftRun> queue) : base(queue) =>
        this.contextFactory = contextFactory;

    public static PostgresStatementDraftStore FromConnectionString(string connectionString) =>
        new(AnalysisDbContextFactory.Create(connectionString));

    public async Task<StatementDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == StatementDraftTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason);
    }

    public async Task<StatementDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var article = await PostgresStatementAnalysisStore.ReadArticleAsync(db, candidateId, cancellationToken);
        if (article is null) return null;
        var gateRun = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .Where(run => run.CandidateId == candidateId && run.Task == StatementGateTask.Task && run.Reason != null)
            .OrderByDescending(run => run.StartedAtUtcTicks).FirstOrDefaultAsync(cancellationToken);
        var existing = await db.Set<StatementDraftRow>().AsNoTracking().Where(row => row.CandidateId == candidateId)
            .Select(row => row.CandidateId).FirstOrDefaultAsync(cancellationToken);
        var revision = await ReadAdmittingRevisionAsync(db, candidateId, cancellationToken);
        return new(article, StatementGateCarryOver.From(gateRun is null ? null : StatementAnalysisQueueMapping.ToRun(gateRun)), existing, revision);
    }

    private static async Task<CollectionConfigurationRevision?> ReadAdmittingRevisionAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken)
    {
        var definitionJson = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                                    join jobAttempt in db.Set<JobAttemptRow>().AsNoTracking() on candidate.AttemptId equals jobAttempt.AttemptId
                                    join job in db.Set<JobRow>().AsNoTracking() on jobAttempt.JobId equals job.JobId
                                    where candidate.CandidateId == candidateId
                                    select job.DefinitionJson).SingleOrDefaultAsync(cancellationToken);
        return definitionJson is null ? null : PostgresCollectionJobStore.ReadDefinition(definitionJson).ConfigurationRevision;
    }

    public async Task<IReadOnlyList<StatementDraftListItem>> ListAsync(StatementDraftView? view, int limit, CancellationToken cancellationToken)
    {
        var entries = await StatementAnalysisListing.ListAsync(contextFactory, StatementDraftTask.Task,
            view is { } selected ? InView(selected) : null, limit, cancellationToken);
        return [.. entries.Select(entry => new StatementDraftListItem(entry.Row.CandidateId, entry.SourceId, entry.Url, entry.Row.Status,
            ViewOf(entry.Row.Status, entry.Row.ErrorCode), entry.Row.ErrorCode, entry.Row.Attempts, entry.LatestRunId))];
    }

    public Task<IReadOnlyDictionary<StatementDraftView, int>> CountAsync(CancellationToken cancellationToken) =>
        StatementAnalysisListing.CountAsync(contextFactory, StatementDraftTask.Task, ViewOf, cancellationToken);

    private static Expression<Func<StatementAnalysisRow, bool>> InView(StatementDraftView view) => view switch
    {
        StatementDraftView.Pending => row => row.Status == State.Pending || row.Status == State.Running ||
            row.Status == State.RetryWaiting || row.Status == State.WaitingForBudget,
        StatementDraftView.Drafted => row => row.Status == State.Succeeded && row.ErrorCode == null,
        StatementDraftView.OverCap => row => row.Status == State.Succeeded && row.ErrorCode == StatementDraftErrorCodes.OverByteCap,
        StatementDraftView.Existing => row => row.Status == State.Succeeded && (row.ErrorCode == StatementDraftErrorCodes.HumanDraftExists ||
            row.ErrorCode == StatementDraftErrorCodes.DraftDiscarded),
        _ => row => row.Status == State.Failed || row.Status == State.Blocked
    };

    private static StatementDraftView ViewOf(State status, string? errorCode) => status switch
    {
        State.Succeeded => errorCode switch
        {
            null => StatementDraftView.Drafted,
            StatementDraftErrorCodes.OverByteCap => StatementDraftView.OverCap,
            _ => StatementDraftView.Existing
        },
        State.Failed or State.Blocked => StatementDraftView.Failed,
        _ => StatementDraftView.Pending
    };
}
