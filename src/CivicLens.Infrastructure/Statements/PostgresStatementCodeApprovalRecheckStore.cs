using System.Data;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresStatementCodeApprovalRecheckStore : IStatementCodeApprovalRecheckStore
{
    private readonly IDbContextFactory<CollectionAttemptDbContext> contextFactory;
    private readonly Func<CollectionAttemptDbContext, string, int, CancellationToken, Task> afterDecision;

    public PostgresStatementCodeApprovalRecheckStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
        afterDecision = AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Statement, PublishedRecordKind.Statement);
    }

    public async Task<IReadOnlyList<StatementCodeApprovalCandidate>> ListAwaitingAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var personDecisions = StatementDraftRows.PersonDecisions(db).AsNoTracking();
        var awaiting = await db.Set<StatementDraftRow>().AsNoTracking()
            .Where(row => row.ReviewStatus == ReviewStatus.AwaitingReview && row.CurrentRevisionNumber >= 1 &&
                !personDecisions.Any(decision => decision.CandidateId == row.CandidateId))
            .OrderBy(row => row.CandidateId)
            .Take(limit)
            .Select(row => new { row.CandidateId, row.CurrentRevisionNumber })
            .ToArrayAsync(cancellationToken);

        var candidates = new List<StatementCodeApprovalCandidate>(awaiting.Length);
        foreach (var row in awaiting)
        {
            var article = await StatementAnalysisListing.ReadArticleAsync(db, row.CandidateId, cancellationToken);
            if (article is null) continue;
            var gateRun = await db.Set<StatementAnalysisRunRow>().AsNoTracking()
                .Where(run => run.CandidateId == row.CandidateId && run.Task == StatementGateTask.Task && run.Reason != null)
                .OrderByDescending(run => run.StartedAtUtcTicks).FirstOrDefaultAsync(cancellationToken);
            var stored = (await StatementRevisionRows.ReadAsync(db,
                db.Set<StatementRevisionRow>().AsNoTracking()
                    .Where(revision => revision.CandidateId == row.CandidateId && revision.RevisionNumber == row.CurrentRevisionNumber),
                cancellationToken)).SingleOrDefault();
            if (stored is null) continue;
            var revision = stored.Read();
            if (revision.AuthorKind == ReviewAuthorKind.Person) continue;
            var evidence = new StatementDraftEvidence(article,
                StatementGateCarryOver.From(gateRun is null ? null : StatementGateQueueMapping.ToRun(gateRun)),
                new(row.CurrentRevisionNumber, IsStaleModelDraft: false), null);
            candidates.Add(new(row.CandidateId, evidence, revision));
        }
        return candidates;
    }

    public async Task<StatementCodeApprovalOutcome> ApplyAsync(string candidateId, int expectedRevisionNumber, ApprovalCheck check,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.StatementReview, cancellationToken);

        var draft = await db.Set<StatementDraftRow>().SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        if (draft is null || draft.ReviewStatus != ReviewStatus.AwaitingReview || draft.CurrentRevisionNumber != expectedRevisionNumber ||
            await StatementDraftRows.PersonDecisions(db).AnyAsync(decision => decision.CandidateId == candidateId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return StatementCodeApprovalOutcome.Skipped;
        }

        var records = PostgresStatementReviewStore.Records;
        var cycle = await records.ReadCycleAsync(db, draft, cancellationToken);
        var stored = (await StatementRevisionRows.ReadAsync(db,
            db.Set<StatementRevisionRow>().AsNoTracking()
                .Where(revision => revision.CandidateId == candidateId && revision.RevisionNumber == expectedRevisionNumber),
            cancellationToken)).SingleOrDefault();
        if (stored is null || !ReviewPolicy.GetUnresolvedConcerns(cycle).IsEmpty)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StatementCodeApprovalOutcome.Skipped;
        }

        var revision = stored.Read();
        if (revision.AuthorKind == ReviewAuthorKind.Person)
        {
            await transaction.RollbackAsync(cancellationToken);
            return StatementCodeApprovalOutcome.Skipped;
        }

        var now = await DatabaseClock.ReadAsync(db, cancellationToken);
        var approved = records.TryCodeApproveCurrent(db, draft, revision, cycle, check, now.Offset);
        if (approved)
            await records.AfterCodeApprovalAsync(db, candidateId, expectedRevisionNumber, afterDecision, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return approved ? StatementCodeApprovalOutcome.Approved : StatementCodeApprovalOutcome.StillHeld;
    }
}
