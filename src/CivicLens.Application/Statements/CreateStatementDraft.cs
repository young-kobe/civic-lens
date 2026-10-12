using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed class CreateStatementDraft(IStatementReviewStore store)
{
    public async Task<StatementDraftRevision> ExecuteAsync(ReviewActor actor, CreateStatementDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var candidateId = ReviewValidation.ValidateCandidateId(request.CandidateId);
        var revision = new StatementDraftRevision(candidateId, 1, actor.Subject, DateTimeOffset.UtcNow, "", "", null, null,
            [], [], [], [], [], []);
        return await store.CreateAsync(actor.Subject, revision, request.IdempotencyKey, ReviewValidation.HashPayload(request),
            cancellationToken);
    }
}
