using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class DecideStatementReview(IStatementReviewStore store, StatementReviewContextReader contextReader)
{
    public async Task<ReviewDecision> ExecuteAsync(ReviewActor actor, DecideStatementReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var candidateId = ReviewValidation.ValidateCandidateId(request.CandidateId);
        var decision = ReviewValidation.CreateDecision(actor, candidateId, request.ExpectedRevisionNumber,
            request.ExpectedReviewStateVersion, request.Kind, request.Note, request.ResolvedDecisionIds);
        var approvalSelections = request.Kind == ReviewDecisionKind.Approve
            ? (await contextReader.ReadAsync(candidateId, cancellationToken)).Allowed : null;
        return await store.DecideAsync(actor.Subject, decision, request.ExpectedRevisionNumber, request.ExpectedReviewStateVersion,
            approvalSelections, request.IdempotencyKey, ReviewValidation.HashPayload(request), cancellationToken);
    }
}
