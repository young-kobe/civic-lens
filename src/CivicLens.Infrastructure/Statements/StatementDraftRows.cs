using System.Text.Json;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal static class StatementDraftRows
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<bool> TryAddAnalysisDraftAsync(CollectionAttemptDbContext db, StatementDraftRevision revision, string runId,
        CancellationToken cancellationToken)
    {
        revision.ValidateAsFirstAnalysisRevision();
        if (await ExistsAsync(db, revision.CandidateId, cancellationToken)) return false;
        await RequireCandidateExtractionAsync(db, revision, cancellationToken);
        db.Add(NewDraft(revision.CandidateId, 1, revision.CreatedAtUtc.UtcTicks));
        db.Add(new StatementRevisionRow
        {
            CandidateId = revision.CandidateId,
            RevisionNumber = 1,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = revision.AuthorKind,
            RunId = runId,
            RevisionJson = JsonSerializer.Serialize(revision, JsonOptions),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        });
        return true;
    }

    public static async Task AddDraftWithoutRevisionAsync(CollectionAttemptDbContext db, string candidateId, long createdAtUtcTicks,
        CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(db, candidateId, cancellationToken)) db.Add(NewDraft(candidateId, 0, createdAtUtcTicks));
    }

    private static StatementDraftRow NewDraft(string candidateId, int currentRevisionNumber, long createdAtUtcTicks) =>
        new() { CandidateId = candidateId, CurrentRevisionNumber = currentRevisionNumber, CreatedAtUtcTicks = createdAtUtcTicks };

    private static Task<bool> ExistsAsync(CollectionAttemptDbContext db, string candidateId, CancellationToken cancellationToken) =>
        db.Set<StatementDraftRow>().AnyAsync(row => row.CandidateId == candidateId, cancellationToken);

    private static async Task RequireCandidateExtractionAsync(CollectionAttemptDbContext db, StatementDraftRevision revision,
        CancellationToken cancellationToken)
    {
        var extractionId = await db.Set<StatementCandidateRow>().Where(row => row.CandidateId == revision.CandidateId)
            .Select(row => row.ExtractionId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("A statement draft needs an existing candidate.", nameof(revision));
        var spans = revision.Quotations.Select(quotation => quotation.Span).Concat(revision.StatementDateEvidence is { } evidence ? [evidence] : []);
        if (spans.Any(span => span.ExtractionId != extractionId))
            throw new ArgumentException("A statement draft can cite only the extraction of its candidate.", nameof(revision));
    }
}
