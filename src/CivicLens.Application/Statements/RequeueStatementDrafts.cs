using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class RequeueStatementDrafts(IStatementDraftStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, string? candidateId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        if (candidateId is not null) ReviewValidation.ValidateHash(candidateId, nameof(candidateId));
        return store.RequeueFailedAsync(candidateId, cancellationToken);
    }
}
