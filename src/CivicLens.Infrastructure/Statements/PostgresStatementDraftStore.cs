using System.Linq.Expressions;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Statements;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementDraftStore : PostgresAnalysisStore<StatementDraftRun>, IStatementDraftStore
{
    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly AdmittingRevisionReader admittingRevisions = new();
    private readonly PostgresAnalysisQueue<StatementAnalysisRow, StatementAnalysisBudgetRow, StatementDraftRun> queue;

    public PostgresStatementDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new StatementDraftQueueMapping(
            AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Statement, PublishedRecordKind.Statement))))
    { }

    private PostgresStatementDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<StatementAnalysisRow, StatementAnalysisBudgetRow, StatementDraftRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

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
        var article = await StatementAnalysisListing.ReadArticleAsync(db, candidateId, cancellationToken);
        if (article is null) return null;
        var gateRun = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
            .Where(run => run.CandidateId == candidateId && run.Task == StatementGateTask.Task && run.Reason != null)
            .OrderByDescending(run => run.StartedAtUtcTicks).FirstOrDefaultAsync(cancellationToken);
        var existing = await ReadExistingDraftAsync(db, candidateId, cancellationToken);
        var revision = await admittingRevisions.ReadAsync(db, candidateId, cancellationToken);
        return new(article, StatementGateCarryOver.From(gateRun is null ? null : StatementGateQueueMapping.ToRun(gateRun)), existing, revision);
    }

    public Task<int> RequeueStaleAsync(CancellationToken cancellationToken) =>
        queue.RequeueAsync(State.Succeeded, null, null, async (db, settled, token) =>
        {
            var stale = await StaleCandidateIdsAsync(db, [.. settled.Select(row => row.CandidateId)], token);
            return settled.Where(row => stale.Contains(row.CandidateId));
        }, cancellationToken);

    private static async Task<StatementExistingDraft?> ReadExistingDraftAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken)
    {
        var current = await db.Set<StatementDraftRow>().AsNoTracking().Where(row => row.CandidateId == candidateId)
            .Select(row => (int?)row.CurrentRevisionNumber).SingleOrDefaultAsync(cancellationToken);
        if (current is not { } number) return null;
        var stale = await StaleCandidateIdsAsync(db, [candidateId], cancellationToken);
        return new(number, stale.Contains(candidateId));
    }

    /// <summary>A stale candidate holds a draft whose current revision is a model revision that an older task, prompt, or schema made.
    /// No person decision has set its status, and no person has approved it. A person approval is final, so code never replaces that text.
    /// A person revision becomes the current revision, so the author kind of the current revision shows that no person revision is on top.</summary>
    private static async Task<HashSet<string>> StaleCandidateIdsAsync(CollectionAttemptDbContext db, string[] candidateIds,
        CancellationToken cancellationToken)
    {
        var untouched = StatementDraftRows.UntouchedStatuses;
        var modelDrafts = await (from draft in db.Set<StatementDraftRow>().AsNoTracking()
                                 join revision in db.Set<StatementRevisionRow>().AsNoTracking()
                                     on new { draft.CandidateId, Number = draft.CurrentRevisionNumber } equals new { revision.CandidateId, Number = revision.RevisionNumber }
                                 where candidateIds.Contains(draft.CandidateId) && untouched.Contains(draft.ReviewStatus) &&
                                     !db.Set<StatementReviewDecisionRow>().Where(decision => decision.CandidateId == draft.CandidateId)
                                         .Any(StatementDraftRows.IsPersonApproval) &&
                                     revision.AuthorKind == ReviewAuthorKind.Analysis && revision.RunId != null
                                 select new { draft.CandidateId, RunId = revision.RunId! }).ToArrayAsync(cancellationToken);
        var staleRunIds = await StaleAnalysisRuns.FindStaleRunIdsAsync<StatementAnalysisRunRow>(db,
            [.. modelDrafts.Select(draft => draft.RunId)], StatementDraftTask.CurrentVersions, cancellationToken);
        return modelDrafts.Where(draft => staleRunIds.Contains(draft.RunId)).Select(draft => draft.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<StatementDraftListItem>> ListAsync(StatementDraftView? view, int limit, CancellationToken cancellationToken)
    {
        var entries = await StatementAnalysisListing.ListAsync(contextFactory, StatementDraftTask.Task,
            view is { } selected ? InView(selected) : null, limit, cancellationToken);
        return [.. entries.Select(entry => new StatementDraftListItem(entry.Row.CandidateId, entry.SourceId, entry.Url, entry.Row.Status,
            ViewOf(entry.Row.Status, entry.Row.ErrorCode), entry.Row.ErrorCode, entry.Row.Attempts, entry.LatestRunId))];
    }

    public async Task<IReadOnlyDictionary<StatementDraftView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AnalysisQueueCounts.CountAsync<StatementAnalysisRow, StatementDraftView>(db, StatementDraftTask.Task, ViewOf, cancellationToken);
    }

    private static Expression<Func<StatementAnalysisRow, bool>> InView(StatementDraftView view) => view switch
    {
        StatementDraftView.Pending => AnalysisQueueStates.IsPending<StatementAnalysisRow>(),
        StatementDraftView.Drafted => row => row.Status == State.Succeeded && row.ErrorCode == null,
        StatementDraftView.OverCap => row => row.Status == State.Succeeded && row.ErrorCode == StatementDraftErrorCodes.OverByteCap,
        StatementDraftView.Existing => row => row.Status == State.Succeeded && (row.ErrorCode == StatementDraftErrorCodes.HumanDraftExists ||
            row.ErrorCode == StatementDraftErrorCodes.DraftDiscarded),
        _ => AnalysisQueueStates.IsFailed<StatementAnalysisRow>()
    };

    private static StatementDraftView ViewOf(State status, string? errorCode) => status switch
    {
        State.Succeeded => errorCode switch
        {
            null => StatementDraftView.Drafted,
            StatementDraftErrorCodes.OverByteCap => StatementDraftView.OverCap,
            _ => StatementDraftView.Existing
        },
        var failed when AnalysisQueueStates.IsFailed(failed) => StatementDraftView.Failed,
        _ => StatementDraftView.Pending
    };
}
