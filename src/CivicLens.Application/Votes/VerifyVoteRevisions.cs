using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class VerifyVoteRevisions(IVoteRevisionBackfillStore store)
{
    private const int ReportedMismatches = 20;

    public Task<VoteRevisionVerification> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.VerifyAsync(ReportedMismatches, cancellationToken);
    }
}
