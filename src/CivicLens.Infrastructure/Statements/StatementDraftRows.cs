using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal static class StatementDraftRows
{
    /// <summary>Appends the revision of the worker under the review lock. The revision must follow the current revision of the draft, so a
    /// person save that came first makes the append fail and write nothing. A draft that a person decided on or approved takes no revision.</summary>
    public static async Task<bool> TryAppendAnalysisRevisionAsync(CollectionAttemptDbContext db, StatementDraftRevision revision, string runId,
        ApprovalCheck codeApproval, CancellationToken cancellationToken)
    {
        revision.ValidateAsAnalysisRevision();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.StatementReview, cancellationToken);
        var draft = await db.Set<StatementDraftRow>().SingleOrDefaultAsync(row => row.CandidateId == revision.CandidateId, cancellationToken);
        if (!FollowsUntouchedDraft(draft, revision)) return false;
        if (draft is not null && await HasPersonApprovalAsync(db, revision.CandidateId, cancellationToken)) return false;
        var capturedOn = await RequireCandidateExtractionAsync(db, revision, cancellationToken);
        var records = PostgresStatementReviewStore.Records;
        var cycle = draft is null ? [] : await records.ReadCycleAsync(db, draft, cancellationToken);
        if (draft is null) db.Add(draft = NewDraft(revision.CandidateId, 1, revision.CreatedAtUtc.UtcTicks));
        SetListing(draft, revision, capturedOn);
        await records.AppendCodeApprovedAsync(db, draft, revision, StatementRevisionRows.Create(revision, runId), cycle,
            () => Task.FromResult(codeApproval));
        return true;
    }

    /// <summary>The statuses that no person decision has set. A rejection, a request for changes, and a withdrawal are decisions of a person.</summary>
    public static readonly ReviewStatus[] UntouchedStatuses = [ReviewStatus.AwaitingReview, ReviewStatus.Approved];

    /// <summary>The approval decisions of a person. <see cref="PersonFinalDecisions"/> owns the author rule, so a code approval is not one and
    /// code may replace a draft that code approved. The status guard already keeps out a person rejection and a person request for changes.</summary>
    public static IQueryable<StatementReviewDecisionRow> PersonApprovals(CollectionAttemptDbContext db) =>
        db.Set<StatementReviewDecisionRow>().Where(PersonFinalDecisions.Match<StatementReviewDecisionRow>())
            .Where(decision => decision.Kind == ReviewDecisionKind.Approve);

    private static Task<bool> HasPersonApprovalAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken) =>
        PersonApprovals(db).AnyAsync(decision => decision.CandidateId == candidateId, cancellationToken);

    private static bool FollowsUntouchedDraft(StatementDraftRow? draft, StatementDraftRevision revision) =>
        draft is null ? revision.RevisionNumber == 1
            : draft.CurrentRevisionNumber == revision.RevisionNumber - 1 && UntouchedStatuses.Contains(draft.ReviewStatus);

    public static async Task AddDraftWithoutRevisionAsync(CollectionAttemptDbContext db, string candidateId, long createdAtUtcTicks,
        CancellationToken cancellationToken)
    {
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.StatementReview, cancellationToken);
        if (await ExistsAsync(db, candidateId, cancellationToken)) return;
        var draft = NewDraft(candidateId, 0, createdAtUtcTicks);
        draft.EventOn = CapturedOn(await db.Set<StatementCandidateRow>().Where(row => row.CandidateId == candidateId)
            .Select(row => row.CreatedAtUtcTicks).SingleAsync(cancellationToken));
        db.Add(draft);
    }

    public static void SetListing(StatementDraftRow draft, StatementDraftRevision revision, DateOnly capturedOn) =>
        ListedDraftRows.Set(draft, revision.EventOn(capturedOn), revision.IssueIds, revision.SpeakerOfficialIds);

    public static StatementDraftRow NewDraft(string candidateId, int currentRevisionNumber, long createdAtUtcTicks) => new()
    {
        CandidateId = candidateId,
        CurrentRevisionNumber = currentRevisionNumber,
        ReviewStatus = ReviewPolicy.GetInitialStatus(currentRevisionNumber),
        CreatedAtUtcTicks = createdAtUtcTicks,
        LastRevisedAtUtcTicks = createdAtUtcTicks
    };

    public static DateOnly CapturedOn(long capturedAtUtcTicks) => DateOnly.FromDateTime(new DateTime(capturedAtUtcTicks, DateTimeKind.Utc));

    public static async Task<DateOnly> RequireCandidateExtractionAsync(CollectionAttemptDbContext db, StatementDraftRevision revision,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Set<StatementCandidateRow>().Where(row => row.CandidateId == revision.CandidateId)
            .Select(row => new { row.ExtractionId, row.CreatedAtUtcTicks }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("A statement draft needs an existing candidate.", nameof(revision));
        var extractionId = candidate.ExtractionId;
        var spans = revision.Quotations.Select(quotation => quotation.Span).Concat(revision.StatementDateEvidence is { } evidence ? [evidence] : []);
        if (spans.Any(span => span.ExtractionId != extractionId))
            throw new ArgumentException("A statement draft can cite only the extraction of its candidate.", nameof(revision));
        return CapturedOn(candidate.CreatedAtUtcTicks);
    }

    private static Task<bool> ExistsAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken) =>
        db.Set<StatementDraftRow>().AnyAsync(row => row.CandidateId == candidateId, cancellationToken);
}
