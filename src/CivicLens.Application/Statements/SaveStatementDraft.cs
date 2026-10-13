using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed class SaveStatementDraft(IStatementReviewStore store, StatementReviewContextReader contextReader)
{
    public async Task<StatementDraftRevision> ExecuteAsync(ReviewActor actor, SaveStatementDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var candidateId = ReviewValidation.ValidateCandidateId(request.CandidateId);
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        if (request.Quotations.IsDefault) throw new ArgumentException("Quotations are required.", nameof(request.Quotations));
        var context = await contextReader.ReadAsync(candidateId, cancellationToken);
        var revision = BuildRevision(actor, request, candidateId, context);
        return await store.SaveRevisionAsync(actor.Subject, revision, request.ExpectedRevisionNumber, request.StatementDateChecked,
            request.IdempotencyKey, ReviewValidation.HashPayload(request), cancellationToken);
    }

    private static StatementDraftRevision BuildRevision(ReviewActor actor, SaveStatementDraftRequest request, string candidateId,
        StatementReviewContext context)
    {
        var allowed = context.Allowed;
        var revision = new StatementDraftRevision(candidateId, request.ExpectedRevisionNumber + 1, actor.Subject, DateTimeOffset.UtcNow,
            request.Headline, request.Summary, request.StatementDate, request.StatementDateEvidence,
            ReviewValidation.ValidateSelections(request.SpeakerOfficialIds, allowed.OfficialIds, nameof(request.SpeakerOfficialIds)),
            request.OtherSpeakers,
            ReviewValidation.ValidateSelections(request.IssueIds, allowed.IssueIds, nameof(request.IssueIds)),
            ReviewValidation.ValidateSelections(request.PolicyAreaIds, allowed.PolicyAreaIds, nameof(request.PolicyAreaIds)),
            [.. request.Quotations.Select(span => new StatementQuotation(span, StatementQuotationKind.ReleaseText))], []);
        revision.ValidateShape();
        revision.ValidateAgainst(context.Article.Extraction);
        return StatementDraftResolver.WithArticleDerivedFields(revision, context.Article.Extraction.Text);
    }
}
