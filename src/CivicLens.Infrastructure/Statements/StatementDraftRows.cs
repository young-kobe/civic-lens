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
        CancellationToken cancellationToken)
    {
        revision.ValidateAsFirstAnalysisRevision();
        await ReviewWriteLock.AcquireAsync(db, ReviewWriteLock.Statement, cancellationToken);
        if (await ExistsAsync(db, revision.CandidateId, cancellationToken)) return false;
        await RequireCandidateExtractionAsync(db, revision, cancellationToken);
        db.Add(NewDraft(revision.CandidateId, 1, revision.CreatedAtUtc.UtcTicks));
        db.Add(NewRevision(revision, runId));
        return true;
    }

    public static async Task AddDraftWithoutRevisionAsync(CollectionAttemptDbContext db, string candidateId, long createdAtUtcTicks,
        CancellationToken cancellationToken)
    {
        await ReviewWriteLock.AcquireAsync(db, ReviewWriteLock.Statement, cancellationToken);
        if (!await ExistsAsync(db, candidateId, cancellationToken)) db.Add(NewDraft(candidateId, 0, createdAtUtcTicks));
    }

    public static StatementDraftRow NewDraft(string candidateId, int currentRevisionNumber, long createdAtUtcTicks) => new()
    {
        CandidateId = candidateId,
        CurrentRevisionNumber = currentRevisionNumber,
        ReviewStatus = ReviewPolicy.GetCurrentStatus(currentRevisionNumber, []),
        CreatedAtUtcTicks = createdAtUtcTicks
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

    public static async Task RequireCandidateExtractionAsync(CollectionAttemptDbContext db, StatementDraftRevision revision,
        CancellationToken cancellationToken)
    {
        var extractionId = await db.Set<StatementCandidateRow>().Where(row => row.CandidateId == revision.CandidateId)
            .Select(row => row.ExtractionId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("A statement draft needs an existing candidate.", nameof(revision));
        var spans = revision.Quotations.Select(quotation => quotation.Span).Concat(revision.StatementDateEvidence is { } evidence ? [evidence] : []);
        if (spans.Any(span => span.ExtractionId != extractionId))
            throw new ArgumentException("A statement draft can cite only the extraction of its candidate.", nameof(revision));
    }

    private static Task<bool> ExistsAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken) =>
        db.Set<StatementDraftRow>().AnyAsync(row => row.CandidateId == candidateId, cancellationToken);
}
