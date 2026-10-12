using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class GetStatementReview(IStatementReviewStore store)
{
    public Task<StatementReviewDetail?> ExecuteAsync(ReviewActor actor, string candidateId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(ReviewValidation.ValidateCandidateId(candidateId), cancellationToken);
    }
}
