using System.Linq.Expressions;
using CivicLens.Application.Actions;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Actions;

public sealed class PostgresActionDraftStore : PostgresAnalysisStore<ActionDraftRun>, IActionDraftStore
{
    public const string Table = "action_analysis";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<ActionAnalysisRow, ActionAnalysisBudgetRow, ActionDraftRun> queue;

    public PostgresActionDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new ActionDraftQueueMapping())) { }

    private PostgresActionDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<ActionAnalysisRow, ActionAnalysisBudgetRow, ActionDraftRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

    public async Task<ActionDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<ActionAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == ActionDraftTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason);
    }

    public async Task<ActionDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<ActionCandidateRow>().AsNoTracking().SingleOrDefaultAsync(item => item.CandidateId == candidateId, cancellationToken);
        if (row is null) return null;
        var recordId = PostgresCollectionJobStore.HashActionRecord(row.SourceId, row.Congress, row.BillType, row.BillNumber);
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, [row.ConfigurationRevisionId], cancellationToken);
        var candidate = new ActionDraftCandidate(row.CandidateId, recordId, row.SourceId, row.Congress, row.BillType, row.BillNumber,
            new DateTimeOffset(row.UpdateDateUtcTicks, TimeSpan.Zero), row.IncludesSummaries);
        return new(candidate, StoredJobDefinition.Find(revisions, row.ConfigurationRevisionId), await ReadPagesAsync(db, row.DetailJobId, row.IncludesSummaries, cancellationToken),
            await ActionDraftRows.ReadCurrentAsync(db, recordId, cancellationToken),
            await ActionDraftRows.HasNewerCandidateAsync(db, candidate, cancellationToken));
    }

    public Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken) =>
        queue.RequeueAsync(State.Blocked, errorCode, null, null, cancellationToken);

    public async Task<IReadOnlyList<ActionDraftListItem>> ListAsync(ActionDraftView? view, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var queue = db.Set<ActionAnalysisRow>().AsNoTracking().Where(row => row.Task == ActionDraftTask.Task);
        if (view is { } selected) queue = queue.Where(InView(selected));
        var rows = await (from entry in queue
                          join candidate in db.Set<ActionCandidateRow>().AsNoTracking() on entry.CandidateId equals candidate.CandidateId
                          orderby entry.CreatedAtUtcTicks, entry.CandidateId
                          select new { Entry = entry, candidate.SourceId, candidate.Congress, candidate.BillType, candidate.BillNumber })
            .Take(limit).ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Entry.CandidateId).ToArray();
        var latest = await AnalysisRunLookup.Latest<ActionAnalysisRunRow>(db, ActionDraftTask.Task, nameof(ActionAnalysisRunRow.CandidateId), ids)
            .Select(run => new { run.CandidateId, run.RunId }).ToDictionaryAsync(run => run.CandidateId, run => run.RunId, cancellationToken);
        return [.. rows.Select(row => new ActionDraftListItem(row.Entry.CandidateId, row.SourceId, $"{row.Congress} {row.BillType} {row.BillNumber}",
            row.Entry.Status, ViewOf(row.Entry.Status, row.Entry.ErrorCode), row.Entry.ErrorCode, row.Entry.Attempts,
            latest.GetValueOrDefault(row.Entry.CandidateId)))];
    }

    public async Task<IReadOnlyDictionary<ActionDraftView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AnalysisQueueCounts.CountAsync<ActionAnalysisRow, ActionDraftView>(db, ActionDraftTask.Task, ViewOf, cancellationToken);
    }

    private static async Task<IReadOnlyList<ActionDraftPage>> ReadPagesAsync(CollectionAttemptDbContext db, string detailJobId,
        bool includesSummaries, CancellationToken cancellationToken)
    {
        var pages = await (from page in db.Set<BillPageRow>().AsNoTracking()
                           join extraction in db.Set<DocumentExtractionRow>().AsNoTracking() on page.ExtractionId equals extraction.ExtractionId
                           where page.DetailJobId == detailJobId && page.Role != BillPageRole.Actions &&
                               (includesSummaries || page.Role != BillPageRole.Summaries)
                           select new { page.Role, page.PageOrder, page.JobId, page.ExtractionId, extraction.ParserVersion, extraction.NormalizationVersion, extraction.Text }).ToArrayAsync(cancellationToken);
        return [.. pages.OrderBy(page => ResourceOf(page.Role)).ThenBy(page => page.PageOrder)
            .Select(page => new ActionDraftPage(ResourceOf(page.Role), page.JobId, page.ExtractionId, page.ParserVersion, page.NormalizationVersion, page.Text))];
    }

    private static ApiResource ResourceOf(BillPageRole role) => role switch
    {
        BillPageRole.Detail => ApiResource.Bill,
        BillPageRole.Cosponsors => ApiResource.BillCosponsors,
        _ => ApiResource.BillSummaries
    };

    private static Expression<Func<ActionAnalysisRow, bool>> InView(ActionDraftView view) => view switch
    {
        ActionDraftView.Pending => AnalysisQueueStates.IsPending<ActionAnalysisRow>(),
        ActionDraftView.Drafted => row => row.Status == State.Succeeded && row.ErrorCode == null,
        ActionDraftView.Unchanged => row => row.Status == State.Succeeded && row.ErrorCode == ActionDraftErrorCodes.Unchanged,
        ActionDraftView.Superseded => row => row.Status == State.Succeeded && row.ErrorCode == ActionDraftErrorCodes.SupersededByNewerCandidate,
        ActionDraftView.NoTrackedOfficial => row => row.Status == State.Succeeded && row.ErrorCode == ActionDraftErrorCodes.NoTrackedOfficial,
        _ => AnalysisQueueStates.IsFailed<ActionAnalysisRow>()
    };

    private static ActionDraftView ViewOf(State status, string? errorCode) => status switch
    {
        State.Succeeded => errorCode switch
        {
            null => ActionDraftView.Drafted,
            ActionDraftErrorCodes.Unchanged => ActionDraftView.Unchanged,
            ActionDraftErrorCodes.NoTrackedOfficial => ActionDraftView.NoTrackedOfficial,
            _ => ActionDraftView.Superseded
        },
        var failed when AnalysisQueueStates.IsFailed(failed) => ActionDraftView.Failed,
        _ => ActionDraftView.Pending
    };
}
