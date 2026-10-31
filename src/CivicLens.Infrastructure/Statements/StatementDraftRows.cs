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
    public static async Task<bool> TryAddAnalysisDraftAsync(CollectionAttemptDbContext db, StatementDraftRevision revision, string runId,
        ApprovalCheck codeApproval, CancellationToken cancellationToken)
    {
        revision.ValidateAsFirstAnalysisRevision();
        await PostgresAdvisoryLock.AcquireAsync(db, PostgresAdvisoryLock.StatementReview, cancellationToken);
        if (await ExistsAsync(db, revision.CandidateId, cancellationToken)) return false;
        var capturedOn = await RequireCandidateExtractionAsync(db, revision, cancellationToken);
        var draft = NewDraft(revision.CandidateId, 1, revision.CreatedAtUtc.UtcTicks);
        SetListing(draft, revision, capturedOn);
        db.Add(draft);
        await PostgresStatementReviewStore.Records.AppendCodeApprovedAsync(db, draft, revision, NewRevision(revision, runId), [],
            () => Task.FromResult(codeApproval), PostgresStatementReviewStore.NewDecisionRow);
        return true;
    }

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

    public static StatementRevisionRow NewRevision(StatementDraftRevision revision, string? runId) => new()
    {
        CandidateId = revision.CandidateId,
        RevisionNumber = revision.RevisionNumber,
        AuthorSubject = revision.AuthorSubject,
        AuthorKind = revision.AuthorKind,
        RunId = runId,
        RevisionJson = ReviewJson.Serialize(revision),
        CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
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
