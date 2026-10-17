using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class GetGateLabelTask(IGateLabelStore store)
{
    public Task<GateLabelTask?> ExecuteAsync(ReviewActor actor, string candidateId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.GetAsync(ReviewValidation.ValidateCandidateId(candidateId), cancellationToken);
    }
}
