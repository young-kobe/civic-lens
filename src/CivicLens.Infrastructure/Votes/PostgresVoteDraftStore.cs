using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.HouseVotes;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

public sealed class PostgresVoteDraftStore : PostgresAnalysisStore<VoteDraftRun>, IVoteDraftStore
{
    public const string Table = "vote_analysis";

    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;

    public PostgresVoteDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
        : this(contextFactory, new(contextFactory, new VoteDraftQueueMapping())) { }

    private PostgresVoteDraftStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory,
        PostgresAnalysisQueue<VoteAnalysisRow, VoteAnalysisBudgetRow, VoteDraftRun> queue) : base(queue) =>
        this.contextFactory = contextFactory;

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
        var row = await db.Set<HouseVoteCandidateRow>().AsNoTracking().SingleOrDefaultAsync(item => item.CandidateId == candidateId, cancellationToken);
        if (row is null) return null;
        var recordId = PostgresCollectionJobStore.HashHouseVoteRecord(row.SourceId, row.Congress, row.SessionNumber, row.RollCallNumber);
        var revisions = await StoredJobDefinition.LoadRevisionsAsync(db, [row.ConfigurationRevisionId], cancellationToken);
        var candidate = new VoteDraftCandidate(row.CandidateId, recordId, row.SourceId, row.Congress, row.SessionNumber, row.RollCallNumber,
            row.BillType, row.BillNumber, new DateTimeOffset(row.UpdateDateUtcTicks, TimeSpan.Zero));
        return new(candidate, StoredJobDefinition.Find(revisions, row.ConfigurationRevisionId),
            await ReadPageAsync(db, row.DetailJobId, row.DetailExtractionId, cancellationToken),
            await ReadPageAsync(db, row.MembersJobId, row.MembersExtractionId, cancellationToken),
            await VoteDraftRows.ReadCurrentAsync(db, recordId, cancellationToken),
            await VoteDraftRows.HasNewerCandidateAsync(db, row.SourceId, row.Congress, row.SessionNumber, row.RollCallNumber,
                row.UpdateDateUtcTicks, cancellationToken));
    }

    private static async Task<VoteDraftPage> ReadPageAsync(CollectionAttemptDbContext db, string jobId, string extractionId,
        CancellationToken cancellationToken)
    {
        var extraction = await db.Set<DocumentExtractionRow>().AsNoTracking()
            .SingleAsync(item => item.ExtractionId == extractionId, cancellationToken);
        return new(jobId, extractionId, extraction.ParserVersion, extraction.NormalizationVersion, extraction.Text);
    }
}
