using System.Linq.Expressions;
using CivicLens.Application.Votes;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Infrastructure.Votes;

public sealed class PostgresVoteDraftStore : PostgresAnalysisStore<VoteDraftRun>, IVoteDraftStore
{
    public const string Table = "vote_analysis";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly PostgresAnalysisQueue<VoteAnalysisRow, VoteAnalysisBudgetRow, VoteDraftRun> queue;

    public PostgresVoteDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, VoteDraftStaleness.NewQueue(contextFactory)) { }

    private PostgresVoteDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<VoteAnalysisRow, VoteAnalysisBudgetRow, VoteDraftRun> queue) : base(queue)
    {
        this.contextFactory = contextFactory;
        this.queue = queue;
    }

    public async Task<VoteDraftRun?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<VoteAnalysisRunRow>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.RunId == runId && candidate.Task == VoteDraftTask.Task, cancellationToken);
        return row is null ? null : new(AnalysisRunRows.ToRun(row), row.Reason);
    }

    public async Task<VoteDraftEvidence?> ReadEvidenceAsync(string candidateId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<VoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(item => item.CandidateId == candidateId, cancellationToken);
        if (row is null) return null;
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, [row.ConfigurationRevisionId], cancellationToken);
        var configuration = StoredJobDefinition.Find(revisions, row.ConfigurationRevisionId);
        var recordKey = VoteSources.RecordKeyOf(configuration.Configuration.Sources.Single(source => source.Id == row.SourceId));
        var recordId = PostgresCollectionJobStore.HashVoteRecord(row.Chamber, recordKey, row.Congress, row.SessionNumber, row.RollCallNumber);
        var candidate = new VoteDraftCandidate(row.CandidateId, recordId, row.SourceId, row.Chamber, row.Congress, row.SessionNumber, row.RollCallNumber,
            row.BillType, row.BillNumber, new DateTimeOffset(row.UpdateDateUtcTicks, TimeSpan.Zero), row.RosterJobId);
        return new(candidate, configuration,
            await ReadPageAsync(db, row.DetailJobId, row.DetailExtractionId, cancellationToken),
            await ReadPageAsync(db, row.MembersJobId, row.MembersExtractionId, cancellationToken),
            await ReadBillDetailAsync(db, row, cancellationToken),
            await VoteDraftRows.ReadCurrentAsync(db, recordId, cancellationToken),
            await VoteDraftRows.HasNewerCandidateAsync(db, row.SourceId, row.Chamber, row.Congress, row.SessionNumber, row.RollCallNumber,
                row.UpdateDateUtcTicks, cancellationToken));
    }

    public Task<int> RequeueBlockedAsync(string errorCode, CancellationToken cancellationToken) =>
        queue.RequeueAsync(State.Blocked, errorCode, null, null, cancellationToken);

    public async Task<int> CountWaitingForBillAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await VoteDraftQueueRows.WaitingForBill(db).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VoteDraftListItem>> ListAsync(VoteDraftView? view, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entries = db.Set<VoteAnalysisRow>().AsNoTracking().Where(row => row.Task == VoteDraftTask.Task);
        if (view is { } selected) entries = entries.Where(InView(selected));
        var rows = await (from entry in entries
                          join candidate in db.Set<VoteCandidateRow>().AsNoTracking() on entry.CandidateId equals candidate.CandidateId
                          orderby entry.CreatedAtUtcTicks, entry.CandidateId
                          select new
                          {
                              Entry = entry,
                              candidate.SourceId,
                              candidate.Chamber,
                              candidate.Congress,
                              candidate.SessionNumber,
                              candidate.RollCallNumber,
                              candidate.BillType,
                              candidate.BillNumber
                          })
            .Take(limit).ToArrayAsync(cancellationToken);
        var ids = rows.Select(row => row.Entry.CandidateId).ToArray();
        var latest = await AnalysisRunLookup.Latest<VoteAnalysisRunRow>(db, VoteDraftTask.Task, nameof(VoteAnalysisRunRow.CandidateId), ids)
            .Select(run => new { run.CandidateId, run.RunId }).ToDictionaryAsync(run => run.CandidateId, run => run.RunId, cancellationToken);
        return [.. rows.Select(row => new VoteDraftListItem(row.Entry.CandidateId, row.SourceId,
            $"{row.Chamber} {row.Congress} {row.SessionNumber} {row.RollCallNumber}", row.BillType is null ? null : $"{row.Congress} {row.BillType} {row.BillNumber}",
            row.Entry.Status, ViewOf(row.Entry.Status, row.Entry.ErrorCode), row.Entry.ErrorCode, row.Entry.Attempts,
            latest.GetValueOrDefault(row.Entry.CandidateId)))];
    }

    public async Task<IReadOnlyDictionary<VoteDraftView, int>> CountAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AnalysisQueueCounts.CountAsync<VoteAnalysisRow, VoteDraftView>(db, VoteDraftTask.Task, ViewOf, cancellationToken);
    }

    private static async Task<VoteDraftPage?> ReadBillDetailAsync(CollectionAttemptDbContext db, VoteCandidateRow candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.BillType is null) return null;
        var page = await BillDetailRows.PagesNewestFirst(db, candidate.Congress, candidate.BillType, candidate.BillNumber!.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return page is null ? null : await ReadPageAsync(db, page.JobId, page.ExtractionId, cancellationToken);
    }

    private static async Task<VoteDraftPage> ReadPageAsync(CollectionAttemptDbContext db, string jobId, string extractionId,
        CancellationToken cancellationToken)
    {
        var extraction = await db.Set<DocumentExtractionRow>().AsNoTracking()
            .SingleAsync(item => item.ExtractionId == extractionId, cancellationToken);
        return new(jobId, extractionId, extraction.ExtractionVersion, extraction.Text);
    }

    private static readonly (VoteDraftView View, string? ErrorCode)[] SucceededViews =
    [
        (VoteDraftView.Drafted, null),
        (VoteDraftView.Unchanged, VoteDraftErrorCodes.Unchanged),
        (VoteDraftView.Superseded, VoteDraftErrorCodes.SupersededByNewerCandidate),
        (VoteDraftView.NoTrackedMember, VoteDraftErrorCodes.NoTrackedMember),
    ];

    private static Expression<Func<VoteAnalysisRow, bool>> InView(VoteDraftView view) => view switch
    {
        VoteDraftView.Pending => AnalysisQueueStates.IsPending<VoteAnalysisRow>(),
        VoteDraftView.Failed => AnalysisQueueStates.IsFailed<VoteAnalysisRow>(),
        _ => AnalysisQueueStates.IsSucceeded<VoteAnalysisRow>(SucceededViews.Single(item => item.View == view).ErrorCode)
    };

    private static VoteDraftView ViewOf(State status, string? errorCode) =>
        status == State.Succeeded ? SucceededViews.Single(item => item.ErrorCode == errorCode).View
            : AnalysisQueueStates.IsFailed(status) ? VoteDraftView.Failed : VoteDraftView.Pending;
}
